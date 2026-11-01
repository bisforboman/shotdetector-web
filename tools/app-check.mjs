// The App's exports under Node, as the page's worker calls them (src/ShotDetectorWeb.App/wwwroot/worker.js): each
// sample opened, then every run of tools/Shared/Runs.cs (RUNS below) detected on it, the scene and stats CSVs and the
// shots as JSON and HTML (ShotsJson, ShotsHtml), the editors' timeline files (TimelineFile) and the YouTube chapters
// (YouTubeChapters) written to the output folder under the names tools/NativeCheck gives its own (tools/check.sh compares
// the two folders); a run paused, split or resumed gives the whole run's timeline files and chapters too. Each
// file to refuse must be refused when opened, or by its run. The shots told while a run goes (the 'shot' import, which the worker posts
// to the page as they come) must be the result's shots, exactly. Files go in as the page's do, a Blob read in slices by .NET's BlobStream
// through the published blob-read.js (tools/node-host.mjs). Fails if the runs load an assembly of FFmpeg's; says whether one was
// loaded at all (interpreted: none), the audio pass included.
// Stop and cancel (Api's Stop and Cancel, which the worker calls on the page's 'stop' and 'cancel' messages, between
// Detect's pauses): every run stopped half-way again, its result the whole run's up to there (checkStop); the first
// sample's first run cancelled half-way, with no result. Detect's pauses (the 'pause' import, the published pause.js as
// in the worker) must take under 1% of the runs' time.
// The sound (Api's Audio, after the runs as the page calls it): every sample's, in the JSON's names the page's panels
// read; for a sample without audio, the core's words the page shows nothing for (NO_AUDIO); the tone sample's waveform
// loud, silent, loud, each edge within a video frame; each waveform against the native check's (--native:
// <sample>.waveform.json), each value within 1; each file of --no-aac answered with why it isn't measured.
// Resume after Stop (Api's Kept, Resume, and Merge with kept bytes; backlog "Resume after Stop, across visits"): the
// default run and the run with every detector stopped at a third of the frames, their kept bytes (a Blob, as the page
// keeps it in IndexedDB) shown again after the file is opened again (Merge of them alone: the stopped run's result),
// resumed and stopped again at two thirds, then opened again and resumed to the end: the whole run's CSVs, shots, JSON,
// HTML and picture measures, the shots told after the stopped part's (up to the first told again) the whole run's;
// stopped in its second range split, the rest split again from the kept bytes' first frame (Ranges from it), merged
// with them: the whole run's too (checkResume). Kept bytes of other options fail with NOT_RESUMED (checkNotResumed).
// A long video across several workers (Api's Ranges, ScoreRange, RangeBytes and Merge, as the page's workers call them):
// the default run, the run with every detector and that run deinterlaced (SPLIT_RUNS) split into ranges at keyframes
// (at most RANGES; Ranges for the run's options, which say how far past a keyframe a range starts), each
// scored on its own and merged from their bytes, must give the whole run's CSVs and shots, the merge telling the shots
// as Detect does (checkRanges); stopped half-way through the second range (and every range after it, as the page stops
// every worker), the merge is the result for the frames from the start without a gap, held to the whole run as a
// stopped run is (checkStopped); ranges scored with other options, or damaged bytes, fail the merge with NOT_MERGED in
// front, on which the page detects again with one worker.
// The video's packets (Api's Packets, the bitrate lane's): every sample's against ffprobe's, whose lines the native
// check wrote (--native: <sample>.packets.txt), exactly.
// The picture's measures (Api's Picture, every run's): the first run's against the native check's (--native:
// <sample>.picture.json, FFmpeg's figures there), exactly, a 10-bit sample's in 10-bit values; a split run's merge the
// whole run's; a stopped run's (one worker or split) for the frames its result read.
// The frame types (Api's FrameTypes, every run's): the first run's ffprobe's pict_type, frame by frame, as the native
// check wrote them (--native: <sample>.frametypes.txt) after comparing them with the core's; a split run's merge the
// whole run's; a stopped run's the whole run's first frames, as many as its result read.
// --light (tools/realworld.sh, for a real file over three minutes; the owner's choice, 2026-10-31): only the default and
// every detector (LIGHT_RUNS), each whole and split into ranges; no cancel, stop, pause or resume, and
// no merges or resumes meant to fail, which the generated samples cover and which don't depend on the file.
//   node tools/app-check.mjs <the publish's wwwroot> <out dir> <sample.mp4|.mov|.avi>... [--reject <file>...]
//     [--no-aac <file>...] [--native <the native check's out dir>] [--light]
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { basename, extname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileBlob, host } from './node-host.mjs';

// tools/Shared/Runs.cs's runs as the page's options: each detector with scenedetect's defaults, all five in one run,
// then deinterlaced (yadif): the default with deinterlace auto, all five with on.
const RUNS = {
  adaptive: { detector: 'adaptive' },
  'adaptive-window': { detector: 'adaptive', frameWindow: 1, minContentVal: 5 },
  content: { detector: 'content' },
  threshold: { detector: 'threshold' },
  histogram: { detector: 'histogram' },
  hash: { detector: 'hash' },
  all: { detectors: ['adaptive', 'content', 'threshold', 'histogram', 'hash'].map(kind => ({ kind })) },
  'deinterlace-auto': { detector: 'adaptive', deinterlace: 'auto' },
  'deinterlace-on': { detectors: ['adaptive', 'content', 'threshold', 'histogram', 'hash'].map(kind => ({ kind })), deinterlace: 'on' },
};
// The runs that deinterlace (where the field order says so, for auto): yadif reads the frame after each, so a stopped
// run's last frame is deinterlaced as a file's last (its own next), and its stats row differs from the whole run's.
const DEINTERLACED = ['deinterlace-auto', 'deinterlace-on'];

// The most ranges a run is split into here, as the page splits a long video (app.js's MOST_WORKERS), and the runs split:
// the page's default (one detector: the merge tells the shots as each cut is confirmed), all five detectors at once
// (each detector's scores merged) and those deinterlaced (wider margins: ShotDetector's RangeFrames), not each detector
// alone again, which would double the check's time. --light splits the first two only.
const RANGES = 4;
const SPLIT_RUNS = ['adaptive', 'all', 'deinterlace-on'];
const LIGHT_RUNS = ['adaptive', 'all'];
// What a merge that fails says first (Api's NotMerged): the page then detects again with one worker.
const NOT_MERGED = 'The ranges didn\'t merge: ';
// What a resume that fails on its kept scores says first (Api's NotResumed): the page then detects from the start.
const NOT_RESUMED = 'The stopped run didn\'t resume: ';

const [wwwroot, outDir, ...rest] = process.argv.slice(2);
const lists = { samples: [], '--reject': [], '--no-aac': [], '--native': [] };
let list = lists.samples, light = false;
for (const arg of rest) {
  if (arg === '--light') light = true;
  else if (arg in lists) list = lists[arg];
  else list.push(arg);
}
const { samples, '--reject': rejects, '--no-aac': noAac, '--native': [nativeDir] } = lists;

let progress = [], told = [], toldAt = [];
// The shots the range from frame 0 told as it scored (scoreRanges): the page shows them while the run goes; the other
// ranges tell none.
let rangeTold = [];
let wholePicture = null; // the sample's first run's Picture(), which every other whole run's must be
let wholeTypes = null;   // and its FrameTypes()
// Detect's pauses (the 'pause' import, pause.js as in the worker): how many, and how long they took in all, and the
// frame to stop the run at, at the first pause from there (as the worker does on a 'stop' message: Api's Stop, between
// two pauses), or to cancel it (Api's Cancel).
let pauses = 0, pausing = 0, stopAt = null, cancelAt = null;
// The frame to pause the run at (the worker's 'pause': pause.js's hold), and what to do once it has been held long
// enough to show it stays still (holding: a callback, run before it is released).
let holdAt = null, holding = null;
// How long a paused run is held before it is released: long enough for frames that kept coming to show.
const HELD_MS = 150;
const { dotnet } = await import(pathToFileURL(resolve(wwwroot, '_framework/dotnet.js')).href);
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
const hostImports = await host(wwwroot);
setModuleImports('host', {
  ...hostImports,
  progress: (done, expected) => progress.push([done, expected]),
  shot: json => { told.push(JSON.parse(json)); toldAt.push(progress.at(-1)?.[0] ?? 0); },
  pause: async framesRead => {
    if (holdAt !== null && framesRead >= holdAt) {
      holdAt = null;
      hostImports.hold();
      const then = holding, reports = progress.length;
      // The callback learns the frame held at and how many progress reports came while held (none, if it stays still).
      setTimeout(() => { then(framesRead, progress.length - reports); hostImports.release(); }, HELD_MS);
    }
    const started = performance.now();
    await hostImports.pause();
    pauses++;
    pausing += performance.now() - started;
    if (stopAt !== null && framesRead >= stopAt) Stop();
    if (cancelAt !== null && framesRead >= cancelAt) Cancel();
  },
});
const { Open, Detect, Stop, Cancel, ScenesCsv, StatsCsv, ShotsJson, ShotsHtml, TimelineFile, YouTubeChapters, Audio, Packets, Picture, FrameTypes, LoadedAssemblies, Ranges, ScoreRange, RangeBytes, Merge, Resume, Kept, Versions } = (await getAssemblyExports(getConfig().mainAssemblyName)).ShotDetectorWeb.App.Api;
await runMain();

// What the core says of a file without audio, which the page's panels show nothing for (panels/waveform.js).
const NO_AUDIO = 'The file has no audio track.';
const ffmpeg = () => LoadedAssemblies().filter(name => name.startsWith('FFmpeg'));
const atStart = ffmpeg();
let failed = false;
const fail = message => { console.error(message); failed = true; };
// The stats CSV's text: StatsCsv gives a Blob of it, written in parts (BlobWriter, README "What the heap holds"), which
// the page downloads as it is and the scores lane reads.
const statsText = () => {
  const blob = StatsCsv();
  if (!(blob instanceof Blob) || blob.type !== 'text/csv')
    throw new Error(`StatsCsv gave ${Object.prototype.toString.call(blob)} of type ${blob?.type}, not a text/csv Blob`);
  return blob.text();
};
// The editors' timeline files (Api's TimelineFile, the run card's downloads for editors; the core's TimelineFiles): each
// format and the extension NativeCheck writes it with, the sample's file name given as the page gives the picked file's.
const TIMELINES = [['edl', 'edl'], ['fcpx', 'fcpxml'], ['fcp7', 'fcp7.xml'], ['otio', 'otio'], ['qp', 'qp'], ['keyframes', 'keyframes.txt'],
  ['shotchanges', 'shotchanges']];
let fileName = ''; // the opened sample's
const timelines = () => TIMELINES.map(([format]) => TimelineFile(format, fileName));
// The YouTube chapters (Api's YouTubeChapters, ShotDetector's Timeline's) at YouTube's minimum and the page's default, as
// NativeCheck writes them: <run>.chapters-<minimum>.json.
const CHAPTERS = [10, 30];
const chapters = () => CHAPTERS.map(minimum => YouTubeChapters(minimum));
// The last run's shots as JSON and HTML (Api's ShotsJson and ShotsHtml, the page's downloads beside the CSVs) and its
// timeline files are a whole run's, byte for byte.
const sameShotFiles = ({ json, html, timelines: whole, chapters: wholeChapters }) => ShotsJson() === json &&
  ShotsHtml() === html && timelines().every((text, i) => text === whole[i]) &&
  chapters().every((text, i) => text === wholeChapters[i]);
let runSeconds = 0, pauseSeconds = 0;

mkdirSync(outDir, { recursive: true });

// The versions the bundle carries (Api's Versions, the page's footer) are the ones Directory.Packages.props pins: a
// central version pins only the packages a project references, so a pin nothing references changes nothing (seen
// 2026-10-29: FrameReader).
const versions = JSON.parse(Versions());
writeFileSync(`${outDir}/versions.json`, JSON.stringify(versions, null, 2));
console.error(`Versions: ${versions.runtime}; ${Object.entries(versions.packages).map(([id, v]) => `${id} ${v}`).join(', ')}`);
const props = readFileSync(new URL('../Directory.Packages.props', import.meta.url), 'utf8');
const pins = Object.fromEntries([...props.matchAll(/<PackageVersion Include="([^"]+)" Version="([^"]+)"/g)].map(m => [m[1], m[2]]));
for (const [id, version] of Object.entries(versions.packages)) {
  // ManagedCodecs.Core comes with each ManagedCodecs package at exactly its own version, so it isn't pinned itself.
  const pinned = pins[id] ?? (id === 'ManagedCodecs.Core' ? pins['ManagedCodecs.H264'] : undefined);
  if (pinned === undefined) fail(`${id} ${version}: not pinned in Directory.Packages.props.`);
  else if (pinned !== version) fail(`${id} ${version}: Directory.Packages.props pins ${pinned}.`);
}
for (const [i, sample] of samples.entries()) {
  const video = JSON.parse(Open(fileBlob(sample)));
  fileName = basename(sample);
  console.error(`${basename(sample)}: ${video.width}x${video.height}, ${video.frameRate.num}/${video.frameRate.den} fps, ${video.frames} frames, ${video.duration} s`);
  // Once: a run cancelled half-way fails as cancelled, keeps no result, and the next run goes on as if it hadn't been.
  if (i === 0 && !light) await checkCancel(sample, video);
  for (const [run, options] of Object.entries(RUNS).filter(([run]) => !light || LIGHT_RUNS.includes(run))) {
    progress = [];
    told = [];
    toldAt = [];
    pauses = pausing = 0;
    const started = performance.now();
    const result = JSON.parse(await Detect(JSON.stringify(options)));
    const seconds = (performance.now() - started) / 1000;
    runSeconds += seconds;
    pauseSeconds += pausing / 1000;
    const name = join(outDir, `${basename(sample, extname(sample))}.${run}`);
    const scenesCsv = ScenesCsv(), statsCsv = await statsText();
    writeFileSync(`${name}.scenes.csv`, scenesCsv);
    writeFileSync(`${name}.stats.csv`, statsCsv);
    const shotFiles = { json: ShotsJson(), html: ShotsHtml(), timelines: timelines(), chapters: chapters() };
    writeFileSync(`${name}.shots.json`, shotFiles.json);
    writeFileSync(`${name}.shots.html`, shotFiles.html);
    TIMELINES.forEach(([, extension], t) => writeFileSync(`${name}.${extension}`, shotFiles.timelines[t]));
    CHAPTERS.forEach((minimum, c) => writeFileSync(`${name}.chapters-${minimum}.json`, shotFiles.chapters[c]));
    // The JSON download is the shots Detect's JSON gives (each one of Shots.Json's array), only indented.
    if (JSON.stringify(JSON.parse(shotFiles.json)) !== JSON.stringify(result.shots))
      fail(`${basename(sample)} ${run}: ShotsJson's shots aren't the result's`);
    // How many shots were told before the last frame's progress: with one detector, all but the last, give or take
    // the progress reports' spacing (about ten a second).
    const early = toldAt.filter(done => done < result.frameCount).length;
    console.error(`${basename(sample)} ${run}: ${result.frameCount} frames, ${result.shots.length} shots ` +
      `(${early} told before the last frame), ${seconds.toFixed(2)} s, ${pauses} pauses ${pausing.toFixed(1)} ms`);
    if (result.stopped !== false) fail(`${basename(sample)} ${run}: a whole run says stopped: ${result.stopped}`);
    // The worker turns these into the page's progress bar: they must come, and the last one says every frame is done.
    const last = progress.at(-1);
    if (last?.[0] !== result.frameCount)
      fail(`${basename(sample)} ${run}: progress ended at ${JSON.stringify(last)}, not at ${result.frameCount} frames`);
    // The page shows these as they come and keeps them when a run is cancelled: they are the result's shots.
    if (JSON.stringify(told) !== JSON.stringify(result.shots))
      fail(`${basename(sample)} ${run}: the shots told were ${JSON.stringify(told)}, the result's ${JSON.stringify(result.shots)}`);
    // The page shows these (panels/warnings.js): ShotDetector's sentences, an empty list when it has none.
    if (!Array.isArray(result.warnings) || result.warnings.some(w => typeof w !== 'string'))
      fail(`${basename(sample)} ${run}: warnings ${JSON.stringify(result.warnings)}, not a list of strings`);
    for (const warning of result.warnings ?? []) console.error(`${basename(sample)} ${run}: warning: ${warning}`);
    if (run === Object.keys(RUNS)[0]) {
      wholePicture = checkPicture(sample, result);
      wholeTypes = checkFrameTypes(sample, result);
    }
    if (!light) await checkStop(sample, video, run, options, result, statsCsv);
    if (run === 'adaptive' && !light) await checkPause(sample, video, options, result, scenesCsv, statsCsv, shotFiles, i === 0);
    if (SPLIT_RUNS.includes(run)) await checkRanges(sample, video, run, options, result, scenesCsv, statsCsv, shotFiles);
    if (SPLIT_RUNS.includes(run) && !light) await checkResume(sample, video, run, options, result, scenesCsv, statsCsv, shotFiles);
  }
  if (!light) {
    await checkNotResumed(sample);
    await checkNotMerged(sample);
  }
  checkAudio(sample, video);
  checkPackets(sample);
}

// Detect's pauses against the runs' time: the pauses must cost under 1% of it (Api's PauseEvery).
const share = pauseSeconds / runSeconds;
console.error(`Pauses: ${(pauseSeconds * 1000).toFixed(0)} ms of ${runSeconds.toFixed(1)} s of runs, ${(share * 100).toFixed(2)}%`);
if (runSeconds > 0 && !(share < 0.01)) fail(`Detect's pauses took ${(share * 100).toFixed(2)}% of the runs' time, not under 1%`);

for (const file of noAac) {
  // Open names the codec as ffprobe does (make-samples.sh: mp3.mp4, opus.mp4, and aac-main.mp4, AAC its decoder refuses).
  const { audio: track } = JSON.parse(Open(fileBlob(file)));
  if (track?.codec !== basename(file).split(/[-.]/)[0])
    fail(`${basename(file)}: Open's audio is ${JSON.stringify(track)}`);
  const audio = JSON.parse(Audio());
  if (typeof audio.notMeasured !== 'string' || audio.track !== null)
    fail(`${basename(file)}: measured (${JSON.stringify(audio).slice(0, 200)}), not answered with a reason`);
  else
    console.error(`${basename(file)}: sound not measured: ${audio.notMeasured}`);
}

// Refused when opened, or, for what only the frames tell (a size that changes midway, shotdetector#625), by the run,
// the promise rejected with the core's message, which the page shows.
for (const file of rejects) {
  let opened = false;
  try {
    Open(fileBlob(file));
    opened = true;
    await Detect(JSON.stringify({}));
    fail(`${basename(file)}: detected, but it should have been refused.`);
  } catch (e) {
    console.error(`${basename(file)}: refused ${opened ? 'by its run' : 'when opened'}: ${e.message}`);
    if (opened && !/not supported/.test(e.message))
      fail(`${basename(file)}: its run failed, not with the core's refusal: ${e.message}`);
  }
}

console.error(`Loaded assemblies: ${LoadedAssemblies().join(', ')}`);
const byRuns = ffmpeg().filter(name => !atStart.includes(name));
if (byRuns.length > 0) {
  fail(`FFmpeg assemblies loaded by the runs: ${byRuns.join(', ')}`);
} else {
  // None at all, interpreted or AOT, since FFmpeg.AutoGen is no longer in the bundle (ShotDetector 3.22.0); before it
  // the AOT runtime loaded it at start. tools/check.sh requires "none at all".
  console.error(atStart.length === 0
    ? 'No FFmpeg assembly loaded at all.'
    : `No FFmpeg assembly loaded by the runs (loaded at start: ${atStart.join(', ')}).`);
}
process.exit(failed ? 1 : 0);

// The run stopped half-way (at the first pause from half its frames): Detect's result for the frames read, as if the
// file ended there. Its stats CSV is the whole run's rows up to there, since a frame's stats depend only on the frames
// before it, but for the adaptive detector's ratio, which waits for its window of frames after it: None in the stopped
// run's last rows. Its shots are the whole run's that end before the stop, then the next one cut short at the stop. A cut
// decided only on frames past the stop is the one difference allowed: a few frames before it, within the adaptive window
// and the minimum shot length (FlashFilter's merge decides a cut that late). (The threshold detector would also end a
// fade-out the stop falls in with a cut, as at a file's end: no sample stops in one.) A deinterlaced run's last row may
// differ too: yadif deinterlaces the last frame read with itself as the next, as a file's last; and so the adaptive
// ratio of the row before the None ones, whose window reaches that frame.
async function checkStop(sample, video, run, options, whole, wholeStats) {
  const name = `${basename(sample)} ${run} stopped`;
  const half = Math.floor(video.frames / 2);
  progress = [];
  told = [];
  stopAt = half;
  let result;
  try {
    result = JSON.parse(await Detect(JSON.stringify(options)));
  } finally {
    stopAt = null;
  }
  const n = result.frameCount;
  if (result.stopped !== true || n < half || n >= whole.frameCount) {
    fail(`${name}: at ${half} of ${whole.frameCount} frames, the result has ${n} frames, stopped ${result.stopped}`);
    return;
  }
  if (progress.at(-1)?.[0] !== n) fail(`${name}: progress ended at ${JSON.stringify(progress.at(-1))}, not at ${n}`);
  if (JSON.stringify(told) !== JSON.stringify(result.shots)) fail(`${name}: the shots told aren't the result's`);
  checkPictureStopped(name, result);
  checkStopped(name, video, result, await statsText(), whole, wholeStats, DEINTERLACED.includes(run));
}

// A run paused half-way (the worker's 'pause') stays where it is while held, no frame read and no progress told, and
// once resumed gives exactly the CSVs, shots, JSON and HTML of the run never paused. On the first sample also a run stopped while
// paused (the worker's 'stop': Api's Stop, then the release), which ends at the frame it was paused at.
async function checkPause(sample, video, options, whole, wholeScenes, wholeStats, wholeShotFiles, alsoStop) {
  const name = `${basename(sample)} adaptive paused`;
  const half = Math.floor(video.frames / 2);
  let still = null;
  progress = [];
  told = [];
  holdAt = half;
  holding = (framesRead, moved) => { still = { framesRead, moved }; };
  const result = JSON.parse(await Detect(JSON.stringify(options)));
  if (still === null) fail(`${name}: never held (at ${half} of ${video.frames} frames)`);
  else if (still.moved > 0) fail(`${name}: ${still.moved} progress reports while held at frame ${still.framesRead}`);
  if (result.stopped !== false || ScenesCsv() !== wholeScenes || await statsText() !== wholeStats ||
    JSON.stringify(result.shots) !== JSON.stringify(whole.shots) || !sameShotFiles(wholeShotFiles))
    fail(`${name}: resumed, the result isn't the run never paused's (stopped ${result.stopped})`);
  else console.error(`${name} at ${still?.framesRead} of ${video.frames} frames, held ${HELD_MS} ms with no ` +
    'frame read, then resumed: the CSVs and shots of the run never paused');
  if (!alsoStop) return;

  progress = [];
  told = [];
  holdAt = half;
  let pausedAt = null;
  holding = framesRead => { pausedAt = framesRead; Stop(); };
  const stopped = JSON.parse(await Detect(JSON.stringify(options)));
  if (stopped.stopped !== true || stopped.frameCount !== pausedAt)
    fail(`${name}, then stopped: ${stopped.frameCount} frames, stopped ${stopped.stopped}, paused at ${pausedAt}`);
  else console.error(`${name}, then stopped: ends at frame ${pausedAt}, where it was paused`);
}

// A stopped run's result (n frames) against the whole run's: its stats and shots as checkStop says.
function checkStopped(name, video, result, statsCsv, whole, wholeStats, deinterlaced = false) {
  const n = result.frameCount, rate = video.frameRate.num / video.frameRate.den;

  // The stats: the whole run's rows for the first n frames, but None for the adaptive ratio in the last w. A row a frame
  // in the order decoded, from frame 0 or (a detector that scores a frame against the one before) frame 1, which the
  // whole run's rows against its frames say: rows by their place, not their Frame Number, which at a variable rate is
  // scenedetect's for the frame's time at the average rate.
  const lines = csv => csv.split(/\r?\n/).filter(line => line !== '');
  const ours = lines(statsCsv), theirs = lines(wholeStats);
  const header = ours[0].split(',');
  const ratios = header.flatMap((h, i) => /^adaptive_ratio.*\(w=(\d+)\)$/.test(h) ? [i] : []);
  const window = ratios.length > 0 ? Number(/\(w=(\d+)\)$/.exec(header[ratios[0]])[1]) : 0;
  const skipped = whole.frameCount - (theirs.length - 1); // 0 or 1: data row i (from 1) is frame i - 1 + skipped
  const rows = Math.min(theirs.length, n - skipped + 1);
  const wrong = [];
  for (let i = 0; i < Math.max(ours.length, rows); i++) {
    if (ours[i] === theirs[i]) continue;
    const a = ours[i]?.split(','), b = theirs[i]?.split(',');
    // Deinterlaced: the last frame's row as a file's last, and the adaptive ratio of the row whose window reaches it.
    if (deinterlaced && i === rows - 1 && a && b && a[0] === b[0]) continue;
    const late = a && b && i < rows && i + skipped > n - window && a.length === b.length &&
      a.every((cell, c) => cell === b[c] || (ratios.includes(c) && cell === 'None'));
    const reaching = deinterlaced && a && b && i < rows && i + skipped > n - window - 1 && a.length === b.length &&
      a.every((cell, c) => cell === b[c] || ratios.includes(c));
    if (reaching) continue;
    if (!late) wrong.push(i);
  }
  if (ours.length !== rows || wrong.length > 0)
    fail(`${name}: ${ours.length - 1} stats rows, not the whole run's ${rows - 1} up to frame ${n}; differing: ` +
      `${wrong.slice(0, 5).map(i => `"${ours[i]}" for "${theirs[i]}"`).join(', ')}`);

  // The shots: the whole run's before the stop, the last one cut short there, but for a cut decided after it.
  const cuts = shots => shots.slice(1).map(s => s.startFrame);
  const lag = window + Math.ceil(0.6 * rate) + 1;
  const kept = result.shots.slice(0, -1), last = result.shots.at(-1);
  const prefix = JSON.stringify(kept) === JSON.stringify(whole.shots.slice(0, kept.length));
  const cutShort = last.startFrame === whole.shots[kept.length]?.startFrame && last.endFrame === n;
  const missed = cuts(whole.shots).filter(c => c > last.startFrame && c <= n);
  if (!prefix || !cutShort || missed.some(c => c <= n - lag)) {
    fail(`${name} at ${n}: shots ${JSON.stringify(result.shots.map(s => [s.startFrame, s.endFrame]))}, the whole ` +
      `run's ${JSON.stringify(whole.shots.map(s => [s.startFrame, s.endFrame]))}`);
  }
  // The JSON and HTML downloads: the stopped run's own shots, as JSON and as a row each of the HTML's shot table.
  const htmlRows = htmlShots(ShotsHtml());
  if (JSON.stringify(JSON.parse(ShotsJson())) !== JSON.stringify(result.shots) || htmlRows !== result.shots.length)
    fail(`${name}: ShotsJson or ShotsHtml (${htmlRows} rows) isn't the stopped run's ${result.shots.length} shots`);
  console.error(`${name} at ${n} of ${whole.frameCount} frames: ${result.shots.length} shots, ` +
    `the whole run's ${kept.length} and the next cut short; ${ours.length - 1} stats rows, the whole run's` +
    `${window > 0 ? `, the adaptive ratio None in the last ${window}` : ''}` +
    `${missed.length > 0 ? `; cuts decided after the stop: ${missed.join(', ')}` : ''}`);
}

// How many shots scenedetect's save-html page (Shots.Html) has: the rows of its last table, the shots, but its header.
function htmlShots(html) {
  return (html.split('<table').at(-1).match(/<tr>/g)?.length ?? 1) - 1;
}

// A run cancelled half-way (Api's Cancel at a pause, as the worker does on a 'cancel' message) fails and keeps no
// result; the file stays open for the next run.
async function checkCancel(sample, video) {
  cancelAt = Math.floor(video.frames / 2);
  let message = null;
  try {
    await Detect(JSON.stringify(RUNS.adaptive));
  } catch (e) {
    message = e.message.split('\n')[0];
  } finally {
    cancelAt = null;
  }
  let kept = null;
  try { kept = ScenesCsv(); } catch { /* no result: as it should be */ }
  if (message !== 'The run was cancelled.' || kept !== null)
    fail(`${basename(sample)}: a run cancelled half-way ended with ${message}, a scenes CSV of ${kept?.length} characters`);
  else console.error(`${basename(sample)}: cancelled half-way: "${message}", no result kept`);
}

// Audio()'s JSON for one opened sample (README "Checks", wasm).
function checkAudio(sample, video) {
  const name = basename(sample);
  const started = performance.now();
  const audio = JSON.parse(Audio());
  const seconds = (performance.now() - started) / 1000;
  const keys = ['notMeasured', 'track', 'waveform', 'referenceTone', 'silences', 'peaks', 'loudness'];
  if (JSON.stringify(Object.keys(audio)) !== JSON.stringify(keys))
    fail(`${name}: Audio() gives ${Object.keys(audio).join(', ')}, not ${keys.join(', ')}`);
  const { track, waveform: w } = audio;
  if (track === null && video.audio !== null && extname(sample) === '.avi') {
    // An AVI's audio is described by Open, not measured (decision 2026-10-30-mjpeg-avi.md): the core's reason.
    if (audio.notMeasured !== `The audio is '${video.audio.codec}' in an AVI file: only AAC in MP4 is measured.` || w !== null)
      fail(`${name}: an AVI's audio, but ${JSON.stringify(audio).slice(0, 200)}`);
    console.error(`${name}: ${video.audio.codec} in AVI, not measured: ${audio.notMeasured}`);
    return;
  }
  if (track === null && video.audio !== null && video.audio.codec !== 'aac') {
    // Audio that isn't AAC (Tears of Steel's MP3, the real-world check) is described by Open, not measured: a reason.
    if (typeof audio.notMeasured !== 'string' || w !== null)
      fail(`${name}: ${video.audio.codec} audio, but ${JSON.stringify(audio).slice(0, 200)}`);
    console.error(`${name}: ${video.audio.codec}, not measured: ${audio.notMeasured}`);
    return;
  }
  if (track === null) {
    // The samples have AAC or no audio at all.
    if (audio.notMeasured !== NO_AUDIO || w !== null) fail(`${name}: no track, but ${JSON.stringify(audio).slice(0, 200)}`);
    if (video.audio !== null) fail(`${name}: Open's audio is ${JSON.stringify(video.audio)}, but the file has none`);
    console.error(`${name}: no audio, ${seconds.toFixed(2)} s`);
    return;
  }
  if (audio.notMeasured !== null) fail(`${name}: a track, and not measured: ${audio.notMeasured}`);
  // Open's audio track, from the headers (NativeCheck checks it against ffprobe's), is the one the decoder measured.
  const said = video.audio && [video.audio.codec, video.audio.sampleRate, video.audio.channels].join(', ');
  if (said !== [track.codec, track.sampleRate, track.channels].join(', '))
    fail(`${name}: Open's audio is ${JSON.stringify(video.audio)}, the measured track ${track.codec}, ${track.sampleRate} Hz, ${track.channels} ch`);
  const points = w.min[0].length;
  console.error(`${name}: ${track.codec}, ${track.sampleRate} Hz, ${track.channels} ch, ${track.duration.toFixed(3)} s ` +
    `from ${track.start.toFixed(3)} s; ${points} points of ${w.samplesPerPoint} samples, ` +
    `${audio.referenceTone.length} tone segments, ${audio.silences.length} silences, ` +
    `integrated ${audio.loudness?.integrated?.toFixed(1)} LUFS; Audio() ${seconds.toFixed(2)} s`);
  if (w.channels !== w.min.length || w.channels !== w.max.length || w.max.some(m => m.length !== points))
    fail(`${name}: the waveform's ${w.channels} channels don't match its arrays`);
  if (Math.abs(w.pointsPerSecond - w.sampleRate / w.samplesPerPoint) > 1e-9) fail(`${name}: pointsPerSecond ${w.pointsPerSecond}`);

  // The same points as natively (the same managed decoder and builder, on another runtime).
  const native = nativeDir && join(nativeDir, `${basename(sample, extname(sample))}.waveform.json`);
  if (native && existsSync(native)) {
    const n = JSON.parse(readFileSync(native, 'utf8'));
    if (n.samplesPerPoint !== w.samplesPerPoint || n.min.length !== w.channels || n.min[0].length !== points) {
      fail(`${name}: the waveform has ${w.channels} x ${points} points of ${w.samplesPerPoint}, natively ` +
        `${n.min.length} x ${n.min[0].length} of ${n.samplesPerPoint}`);
    } else {
      let differing = 0, largest = 0;
      for (const [ours, theirs] of [[w.min, n.min], [w.max, n.max]]) {
        ours.forEach((values, c) => values.forEach((v, i) => {
          const d = Math.abs(v - theirs[c][i]);
          if (d > 0) differing++;
          largest = Math.max(largest, d);
        }));
      }
      if (largest > 1) fail(`${name}: the waveform's values are up to ${largest} from the native ones`);
      console.error(`${name}: the waveform against the native one: ${differing} values differ, by up to ${largest}`);
    }
  } else if (nativeDir) {
    fail(`${name}: no native waveform (${native})`);
  }

  if (name.startsWith('tone-silence')) checkTone(name, track, w, video.frameRate.den / video.frameRate.num);
}

// The tone sample's envelope (make-samples.sh): 6 s of the tone (a peak of 0.1, -20 dBFS) from 0, digital silence, then
// 6 s of the tone again to the end (the gap is the whole seconds of the track but those 12). Every point whose span lies
// a video frame or more inside a part is loud (above -26 dBFS) or silent (below -60 dBFS), so each edge is where it
// should be within a frame.
function checkTone(name, track, w, frame) {
  const loud = 0.05 * 32767, silent = 0.001 * 32767;
  const end = Math.round(track.start + track.duration), gap = end - 12;
  const parts = [[0, 6, 'loud'], [6, 6 + gap, 'silent'], [6 + gap, end, 'loud']];
  if (gap < 1 || Math.abs(track.start + track.duration - end) > frame)
    fail(`${name}: the audio ends at ${track.start + track.duration} s, not a whole second over 13`);
  const wrong = [];
  for (let i = 0; i < w.min[0].length; i++) {
    const from = w.start + i / w.pointsPerSecond, to = from + 1 / w.pointsPerSecond;
    const peak = Math.max(...w.min.map(m => -m[i]), ...w.max.map(m => m[i]));
    const part = parts.find(([a, b]) => from >= a + frame && to <= b - frame);
    if (part && (part[2] === 'loud' ? peak < loud : peak >= silent))
      wrong.push(`${from.toFixed(3)} s: ${peak} (should be ${part[2]})`);
  }
  const at = parts.map(p => p[0]).concat(end).join(', ');
  if (wrong.length > 0) fail(`${name}: the waveform isn't loud, silent, loud at ${at} s: ${wrong.slice(0, 5).join('; ')}`);
  else console.error(`${name}: the waveform is loud, silent, loud at ${at} s, within a frame (${frame} s)`);
}

// Packets()'s JSON for one opened sample against ffprobe -show_packets -select_streams v:0 (the native check's
// PacketCheck.cs, which compares VideoInput.Packets with the same lines): the same packets in the same order, each with
// the same pts, pts_time (pts × num / den, as ffprobe prints it), size, and K and D flags; the keyframes and the
// discarded packets as indexes, in order; an AVI's offsets (where the page cuts each JPEG from the file) each at bytes
// that hash to ffprobe's data_hash of the packet, an MP4's null.
function checkPackets(sample) {
  const name = basename(sample);
  const started = performance.now();
  const json = Packets();
  const p = JSON.parse(json);
  const seconds = (performance.now() - started) / 1000;
  const keys = ['timeBase', 'pts', 'sizes', 'keyframes', 'discarded', 'offsets'];
  if (JSON.stringify(Object.keys(p)) !== JSON.stringify(keys))
    return fail(`${name}: Packets() gives ${Object.keys(p).join(', ')}, not ${keys.join(', ')}`);
  const { timeBase: { num, den }, pts, sizes, keyframes, discarded, offsets } = p;
  const bytes = sizes.reduce((a, b) => a + b, 0);
  console.error(`${name}: ${pts.length} video packets, ${keyframes.length} keyframes, ${discarded.length} discarded, ` +
    `${bytes} bytes; Packets() ${json.length} characters, ${seconds.toFixed(3)} s`);
  const native = nativeDir && join(nativeDir, `${basename(sample, extname(sample))}.packets.txt`);
  if (!native || !existsSync(native)) {
    if (nativeDir) fail(`${name}: no ffprobe packets from the native check (${native})`);
    return;
  }
  const theirs = readFileSync(native, 'utf8').split('\n').filter(line => line.trim() !== '')
    .map(line => Object.fromEntries(line.trim().split('|').map(f => f.split('=', 2))));
  const isKey = new Set(keyframes), isDiscarded = new Set(discarded);
  const differences = [];
  if (pts.length !== theirs.length) differences.push(`${pts.length} packets vs ffprobe's ${theirs.length}`);
  if (sizes.length !== pts.length) differences.push(`${sizes.length} sizes for ${pts.length} packets`);
  const avi = extname(sample).toLowerCase() === '.avi';
  if (avi ? offsets?.length !== pts.length : offsets !== null)
    differences.push(`offsets ${offsets === null ? 'null' : `for ${offsets.length} packets`} in ${avi ? 'an AVI' : 'an MP4'}`);
  const file = offsets ? readFileSync(sample) : null;
  for (const indexes of [keyframes, discarded]) {
    if (indexes.some((i, j) => !Number.isInteger(i) || i < 0 || i >= pts.length || (j > 0 && i <= indexes[j - 1])))
      differences.push(`indexes not increasing within the packets: ${indexes.slice(0, 10).join(', ')}`);
  }
  for (let i = 0; i < Math.min(pts.length, theirs.length); i++) {
    const t = theirs[i];
    const check = (what, ours, ffprobe) => { if (ours !== ffprobe) differences.push(`packet ${i}: ${what} ${ours} vs ffprobe's ${ffprobe}`); };
    check('pts', String(pts[i]), t.pts);
    check('pts_time', (pts[i] * (num / den)).toFixed(6), t.pts_time);
    check('size', String(sizes[i]), t.size);
    check('K', isKey.has(i) ? 'K' : '_', t.flags[0]);
    check('D', isDiscarded.has(i) ? 'D' : '_', t.flags[1]);
    if (offsets && i < offsets.length)
      check('the bytes at its offset\'s data_hash',
        `SHA256:${createHash('sha256').update(file.subarray(offsets[i], offsets[i] + sizes[i])).digest('hex')}`, t.data_hash);
  }
  if (differences.length > 0)
    fail(`${name}: Packets() differs from ffprobe's: ${differences.slice(0, 3).join('; ')}` +
      `${differences.length > 3 ? `; ${differences.length - 3} more` : ''}`);
  else
    console.error(`${name}: Packets() against ffprobe's ${theirs.length} packets: the same`);
}

// Scores every range of Ranges(RANGES) one after another on this runtime, as the page's workers each score one, each
// stopped at half its frames from range `stopFrom` on (as the page stops every worker at once): each one's bytes.
async function scoreRanges(options, plan, stopFrom = plan.ranges.length, from = 0) {
  const bytes = [];
  rangeTold = [];
  for (const [i, { firstFrame, endFrame }] of plan.ranges.entries()) {
    stopAt = i >= stopFrom ? Math.floor((endFrame - firstFrame) / 2) : null;
    told = [];
    try {
      const scored = JSON.parse(await ScoreRange(JSON.stringify(options), RANGES, i, from));
      if (scored.stopped !== (stopAt !== null)) fail(`range ${i}: stopped ${scored.stopped}`);
    } finally {
      stopAt = null;
    }
    if (firstFrame === 0) rangeTold = told;
    else if (told.length > 0) fail(`range ${i} (from frame ${firstFrame}): told ${told.length} shots, not none`);
    bytes.push(RangeBytes());
  }
  return bytes;
}

// Merge(options, a stopped run's kept bytes, the ranges' bytes one after another, their lengths), as the page's worker
// is called.
function merge(options, bytes, kept = new Uint8Array(0)) {
  const joined = new Uint8Array(bytes.reduce((n, b) => n + b.length, 0));
  let at = 0;
  for (const b of bytes) {
    joined.set(b, at);
    at += b.length;
  }
  told = [];
  return JSON.parse(Merge(JSON.stringify(options), kept, joined, bytes.map(b => b.length)));
}

// The run split into ranges at keyframes, scored range by range and merged: the whole run's CSVs, shots (and their JSON
// and HTML) and frames, the
// shots told by the merge; then stopped half-way through the second range (the first, with one), the result for the
// frames from the start.
async function checkRanges(sample, video, run, options, whole, wholeScenes, wholeStats, wholeShotFiles) {
  const name = `${basename(sample)} ${run} in ranges`;
  const plan = JSON.parse(Ranges(JSON.stringify(options), RANGES, 0));
  if (plan.frames !== whole.frameCount) fail(`${name}: Ranges says ${plan.frames} frames, the whole run read ${whole.frameCount}`);
  const started = performance.now();
  const bytes = await scoreRanges(options, plan);
  const result = merge(options, bytes);
  const seconds = (performance.now() - started) / 1000;
  const same = ScenesCsv() === wholeScenes && await statsText() === wholeStats && sameShotFiles(wholeShotFiles);
  if (!same || JSON.stringify(result.shots) !== JSON.stringify(whole.shots) || result.frameCount !== whole.frameCount ||
    result.stopped !== false || JSON.stringify(result.warnings) !== JSON.stringify(whole.warnings))
    fail(`${name}: ${same ? 'the same CSVs, JSON and HTML' : 'other CSVs, JSON or HTML'}, ${result.shots.length} shots of ${result.frameCount} frames ` +
      `(stopped ${result.stopped}), the whole run's ${whole.shots.length} of ${whole.frameCount}`);
  // The first range's shots told as it scored, then the merge's, each replacing those from its number on, as the page
  // shows them: the result's; and the merge doesn't tell again what the first range told and the result has.
  const page = shown(rangeTold, told);
  const again = told.length > 0 && JSON.stringify(rangeTold[told[0].shot - 1]) === JSON.stringify(told[0]);
  if (JSON.stringify(page) !== JSON.stringify(result.shots) || again)
    fail(`${name}: the shots told (${rangeTold.length} by the first range, ${told.length} by the merge) aren't its result's`);
  const live = told.length > 0 ? told[0].shot - 1 : rangeTold.length;
  console.error(`${name}: ${rangeTold.length} shots told by the first range as it scored, the merge's from shot ${live + 1}`);
  if (Picture() !== wholePicture) fail(`${name}: the merged picture measures aren't the whole run's`);
  if (FrameTypes() !== wholeTypes) fail(`${name}: the merged frame types aren't the whole run's`);
  console.error(`${name}: ${plan.ranges.map(r => `${r.firstFrame}-${r.endFrame - 1}`).join(', ')}, ` +
    `${bytes.map(b => b.length).join(' + ')} bytes, ${seconds.toFixed(2)} s: ${same ? 'the whole run\'s CSVs' : 'DIFFERENT CSVs'}`);

  const stopFrom = Math.min(1, plan.ranges.length - 1), stopped = plan.ranges[stopFrom];
  const partial = merge(options, await scoreRanges(options, plan, stopFrom));
  // The range's frames start before its first (one frame, two deinterlaced): stopped after half of them (scoreRanges), at
  // least this many.
  const before = DEINTERLACED.includes(run) ? 2 : 1;
  const n = partial.frameCount, half = stopped.firstFrame - before + Math.floor((stopped.endFrame - stopped.firstFrame) / 2);
  if (partial.stopped !== true || n < half || n >= stopped.endFrame) {
    fail(`${name} stopped in range ${stopFrom} (${stopped.firstFrame}-${stopped.endFrame - 1}) at ${half}: ` +
      `${n} frames, stopped ${partial.stopped}`);
    return;
  }
  checkPictureStopped(`${name} stopped in range ${stopFrom}`, partial);
  checkStopped(`${name} stopped in range ${stopFrom}`, video, partial, await statsText(), whole, wholeStats,
    DEINTERLACED.includes(run));
}

// The whole run's picture measures (Api's Picture): the native check's, which it compared with FFmpeg's filters,
// exactly (the same JSON); every frame of the run's. Returns them, for the other runs of the sample.
function checkPicture(sample, result) {
  const json = Picture(), picture = JSON.parse(json), name = `${basename(sample)} picture`;
  if (picture.frames !== result.frameCount) fail(`${name}: ${picture.frames} frames, the run read ${result.frameCount}`);
  // A 10-bit sample measured in 10-bit code values (backlog-done "Picture measures at 10 bits"), the others in 8-bit.
  const depth = basename(sample).startsWith('yuv420p10-') ? 10 : 8;
  if (picture.bitDepth !== depth || (picture.frames > 0 && picture.levels.max === null))
    fail(`${name}: measured at ${picture.bitDepth} bits (levels ${JSON.stringify(picture.levels)}), not ${depth}`);
  const native = join(nativeDir ?? '', `${basename(sample, extname(sample))}.picture.json`);
  if (!nativeDir || !existsSync(native)) fail(`${name}: no native ${native} to compare with`);
  else if (readFileSync(native, 'utf8') !== json) fail(`${name}: differs from the native check's ${native}`);
  else {
    console.error(`${name}: the native check's (${picture.bitDepth}-bit, YMAX up to ${picture.levels.max}, ${picture.black.length} black spans, ${picture.bars.length} colour bars, ` +
      `BRNG in ${picture.brng.frames} frames, ${picture.crop ? `crop=${picture.crop.w}:${picture.crop.h}:${picture.crop.x}:${picture.crop.y}` : 'no crop'})`);
  }
  return json;
}

// A stopped run's picture measures and frame types: the frames its result read, the types the whole run's first.
function checkPictureStopped(name, result) {
  const picture = JSON.parse(Picture());
  if (picture.frames !== result.frameCount) fail(`${name}: picture measures of ${picture.frames} frames, the result read ${result.frameCount}`);
  const types = FrameTypes();
  if (types !== wholeTypes?.slice(0, result.frameCount))
    fail(`${name}: frame types of ${types.length} frames, not the whole run's first ${result.frameCount}`);
}

// The whole run's frame types (Api's FrameTypes): ffprobe's pict_type of every frame, as the native check wrote them
// once the core's were the same. Returns them, for the other runs of the sample.
function checkFrameTypes(sample, result) {
  const types = FrameTypes(), name = `${basename(sample)} frame types`;
  if (types.length !== result.frameCount) fail(`${name}: ${types.length} frames, the run read ${result.frameCount}`);
  const native = join(nativeDir ?? '', `${basename(sample, extname(sample))}.frametypes.txt`);
  if (!nativeDir || !existsSync(native)) fail(`${name}: no native ${native} to compare with`);
  else if (readFileSync(native, 'utf8') !== types) fail(`${name}: differ from ffprobe's in ${native}`);
  else console.error(`${name}: ffprobe's (${[...'IPB'].map(t => `${types.split(t).length - 1} ${t}`).join(', ')})`);
  return types;
}

// Ranges that don't make up the video fail the merge with NOT_MERGED and ShotDetector's reason: scored with other
// options than the merge's (another fingerprint), or their bytes damaged (the checksum).
async function checkNotMerged(sample) {
  const plan = JSON.parse(Ranges(JSON.stringify(RUNS.content), RANGES, 0));
  const bytes = await scoreRanges(RUNS.content, plan);
  const damaged = bytes.map(b => b.slice());
  damaged.at(-1)[damaged.at(-1).length >> 1] ^= 1;
  for (const [what, options, b] of [['other options', RUNS.adaptive, bytes], ['damaged bytes', RUNS.content, damaged]]) {
    let message = null;
    try {
      merge(options, b);
    } catch (e) {
      message = e.message.split('\n')[0];
    }
    if (!message?.startsWith(NOT_MERGED)) fail(`${basename(sample)}: ranges with ${what} merged, or failed otherwise: ${message}`);
    else console.error(`${basename(sample)}: ranges with ${what} don't merge: "${message}"`);
  }
}

// A stopped run's kept bytes (Api's Kept: a Blob, which the page keeps in IndexedDB), and the frame its rest starts at
// (their first four bytes).
async function kept() {
  const blob = Kept();
  if (!(blob instanceof Blob)) throw new Error(`Kept gave ${Object.prototype.toString.call(blob)}, not a Blob`);
  const bytes = new Uint8Array(await blob.arrayBuffer());
  return { bytes, from: new DataView(bytes.buffer).getInt32(0, true) };
}

// The shots the page shows across a resume: the stopped part's, then each told replacing those from its number on (the
// last one, cut short at the stop, is told again to where it ends).
function shown(stoppedShots, toldShots) {
  const shots = [...stoppedShots];
  for (const shot of toldShots) shots.splice(shot.shot - 1, Infinity, shot);
  return shots;
}

async function checkResume(sample, video, run, options, whole, wholeScenes, wholeStats, wholeShotFiles) {
  const name = `${basename(sample)} ${run} resumed`, json = JSON.stringify(options);
  const same = async r => ScenesCsv() === wholeScenes && await statsText() === wholeStats && sameShotFiles(wholeShotFiles) &&
    JSON.stringify(r.shots) === JSON.stringify(whole.shots) && r.frameCount === whole.frameCount && r.stopped === false &&
    Picture() === wholePicture && FrameTypes() === wholeTypes;

  // Stopped at a third, opened again (a later visit) and shown from the kept bytes, resumed and stopped at two thirds.
  told = [];
  stopAt = Math.floor(video.frames / 3);
  let first;
  try { first = JSON.parse(await Detect(json)); } finally { stopAt = null; }
  const keep1 = await kept(), scenes1 = ScenesCsv(), stats1 = await statsText();
  if (first.stopped !== true) return fail(`${name}: stopped at a third, the run says stopped ${first.stopped}`);
  Open(fileBlob(sample));
  const again = merge(options, [], keep1.bytes);
  if (JSON.stringify(again) !== JSON.stringify({ ...first }) || ScenesCsv() !== scenes1 || await statsText() !== stats1)
    fail(`${name}: the kept bytes shown again aren't the stopped run's result (${again.frameCount} frames, ${first.frameCount} stopped)`);
  let shots = first.shots;
  progress = [];
  told = [];
  stopAt = Math.floor(video.frames * 2 / 3) - Math.max(keep1.from - 1, 0);
  let second;
  try { second = JSON.parse(await Resume(json, keep1.bytes)); } finally { stopAt = null; }
  shots = shown(shots, told);
  if (second.stopped !== true || second.frameCount <= first.frameCount)
    return fail(`${name}: resumed from ${keep1.from} and stopped at two thirds: ${second.frameCount} frames, stopped ${second.stopped}`);
  if (JSON.stringify(shots) !== JSON.stringify(second.shots)) fail(`${name}: stopped again, the shots shown aren't its result's`);
  if (progress.at(-1)?.[0] !== second.frameCount) fail(`${name}: stopped again, progress ended at ${JSON.stringify(progress.at(-1))}`);
  checkStopped(`${name}, stopped again`, video, second, await statsText(), whole, wholeStats, DEINTERLACED.includes(run));
  const keep2 = await kept();

  // Opened again and resumed to the end: the whole run's.
  Open(fileBlob(sample));
  progress = [];
  told = [];
  const started = performance.now();
  const last = JSON.parse(await Resume(json, keep2.bytes));
  const seconds = (performance.now() - started) / 1000;
  shots = shown(shots, told);
  if (!await same(last)) fail(`${name}: resumed to the end, not the whole run's CSVs, shots, JSON, HTML or picture`);
  if (JSON.stringify(shots) !== JSON.stringify(whole.shots)) fail(`${name}: the shots shown across the resumes aren't the whole run's`);
  if (progress.at(-1)?.[0] !== whole.frameCount) fail(`${name}: progress ended at ${JSON.stringify(progress.at(-1))}`);
  const kept3 = await kept();
  if (kept3.from !== -1) fail(`${name}: a finished run's kept bytes say its rest starts at ${kept3.from}`);
  console.error(`${name}: stopped at ${first.frameCount}, resumed from ${keep1.from} (${keep1.bytes.length} bytes kept), ` +
    `stopped at ${second.frameCount}, resumed from ${keep2.from} (${keep2.bytes.length} bytes) to the end in ` +
    `${seconds.toFixed(2)} s: the whole run's`);

  // Split: stopped in its second range, the rest split again from the kept bytes' first frame.
  const plan = JSON.parse(Ranges(json, RANGES, 0));
  if (plan.ranges.length < 2) return;
  const partial = merge(options, await scoreRanges(options, plan, 1));
  const keep = await kept();
  Open(fileBlob(sample));
  const rest = JSON.parse(Ranges(json, RANGES, keep.from));
  if (rest.ranges[0].firstFrame !== keep.from) fail(`${name} split: the rest's ranges start at ${rest.ranges[0].firstFrame}, not ${keep.from}`);
  const merged = merge(options, await scoreRanges(options, rest, rest.ranges.length, keep.from), keep.bytes);
  if (!await same(merged)) fail(`${name} split: the rest merged with the kept bytes isn't the whole run's`);
  else console.error(`${name} split: stopped at ${partial.frameCount}, the rest from ${keep.from} in ` +
    `${rest.ranges.map(r => `${r.firstFrame}-${r.endFrame - 1}`).join(', ')}: the whole run's`);
}

// A stopped run's kept bytes resumed with other options than its own: NOT_RESUMED and ShotDetector's reason. Stopped
// at 90% of the frames, past the last place a range can start on every sample (the tone sample's is frame 252 of
// 375): kept bytes with no place to resume from but frame 0 start over, and nothing of theirs is compared.
async function checkNotResumed(sample) {
  stopAt = Math.floor(JSON.parse(Ranges(JSON.stringify(RUNS.content), 1, 0)).frames * 9 / 10);
  try { await Detect(JSON.stringify(RUNS.content)); } finally { stopAt = null; }
  const { bytes, from } = await kept();
  if (from <= 0) return console.error(`${basename(sample)}: kept bytes of other options: no place to resume from but frame 0`);
  let message = null;
  try {
    await Resume(JSON.stringify(RUNS.adaptive), bytes);
  } catch (e) {
    message = e.message.split('\n')[0];
  }
  if (!message?.startsWith(NOT_RESUMED)) fail(`${basename(sample)}: kept bytes of other options resumed, or failed otherwise: ${message}`);
  else console.error(`${basename(sample)}: kept bytes of other options don't resume: "${message}"`);
}
