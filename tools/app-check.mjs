// The App's exports under Node, as the page's worker calls them (src/ShotDetectorWeb.App/wwwroot/worker.js): each
// sample opened, then every run of tools/Shared/Runs.cs (RUNS below) detected on it, the scene and stats CSVs written to
// the output folder under the names tools/NativeCheck gives its own (tools/check.sh compares the two folders). Each
// file to refuse must be refused when opened. The shots told while a run goes (the 'shot' import, which the worker posts
// to the page as they come) must be the result's shots, exactly. Files go in as the page's do, a Blob read in slices by .NET's BlobStream
// through the published blob-read.js (tools/node-host.mjs). Fails if the runs load an assembly of FFmpeg's; says whether one was
// loaded at all (interpreted: none), the audio pass included.
// The sound (Api's Audio, after the runs as the page calls it): every sample's, in the JSON's names the page's panels
// read; for a sample without audio, the core's words the page shows nothing for (NO_AUDIO); the tone sample's waveform
// loud, silent, loud, each edge within a video frame; each waveform against the native check's (--native:
// <sample>.waveform.json), each value within 1; each file of --no-aac answered with why it isn't measured.
//   node tools/app-check.mjs <the publish's wwwroot> <out dir> <sample.mp4>... [--reject <file>...]
//     [--no-aac <file>...] [--native <the native check's out dir>]
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, extname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileBlob, host } from './node-host.mjs';

// tools/Shared/Runs.cs's runs as the page's options: each detector with scenedetect's defaults, and all five in one run.
const RUNS = {
  adaptive: { detector: 'adaptive' },
  content: { detector: 'content' },
  threshold: { detector: 'threshold' },
  histogram: { detector: 'histogram' },
  hash: { detector: 'hash' },
  all: { detectors: ['adaptive', 'content', 'threshold', 'histogram', 'hash'].map(kind => ({ kind })) },
};

const [wwwroot, outDir, ...rest] = process.argv.slice(2);
const lists = { samples: [], '--reject': [], '--no-aac': [], '--native': [] };
let list = lists.samples;
for (const arg of rest) {
  if (arg in lists) list = lists[arg];
  else list.push(arg);
}
const { samples, '--reject': rejects, '--no-aac': noAac, '--native': [nativeDir] } = lists;

let progress = [], told = [], toldAt = [];
const { dotnet } = await import(pathToFileURL(resolve(wwwroot, '_framework/dotnet.js')).href);
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('host', {
  ...await host(wwwroot),
  progress: (done, expected) => progress.push([done, expected]),
  shot: json => { told.push(JSON.parse(json)); toldAt.push(progress.at(-1)?.[0] ?? 0); },
});
const { Open, Detect, ScenesCsv, StatsCsv, Audio, LoadedAssemblies } = (await getAssemblyExports(getConfig().mainAssemblyName)).ShotDetectorWeb.App.Api;
await runMain();

// What the core says of a file without audio, which the page's panels show nothing for (panels/waveform.js).
const NO_AUDIO = 'The file has no audio track.';
const ffmpeg = () => LoadedAssemblies().filter(name => name.startsWith('FFmpeg'));
const atStart = ffmpeg();
let failed = false;
const fail = message => { console.error(message); failed = true; };

mkdirSync(outDir, { recursive: true });
for (const sample of samples) {
  const video = JSON.parse(Open(fileBlob(sample)));
  console.error(`${basename(sample)}: ${video.width}x${video.height}, ${video.frameRate.num}/${video.frameRate.den} fps, ${video.frameCount} frames, ${video.duration} s`);
  for (const [run, options] of Object.entries(RUNS)) {
    progress = [];
    told = [];
    toldAt = [];
    const started = performance.now();
    const result = JSON.parse(Detect(JSON.stringify(options)));
    const seconds = (performance.now() - started) / 1000;
    const name = join(outDir, `${basename(sample, extname(sample))}.${run}`);
    writeFileSync(`${name}.scenes.csv`, ScenesCsv());
    writeFileSync(`${name}.stats.csv`, StatsCsv());
    // How many shots were told before the last frame's progress: with one detector, all but the last, give or take
    // the progress reports' spacing (about ten a second).
    const early = toldAt.filter(done => done < result.frameCount).length;
    console.error(`${basename(sample)} ${run}: ${result.frameCount} frames, ${result.shots.length} shots ` +
      `(${early} told before the last frame), ${seconds.toFixed(2)} s`);
    // The worker turns these into the page's progress bar: they must come, and the last one says every frame is done.
    const last = progress.at(-1);
    if (last?.[0] !== result.frameCount)
      fail(`${basename(sample)} ${run}: progress ended at ${JSON.stringify(last)}, not at ${result.frameCount} frames`);
    // The page shows these as they come and keeps them when a run is cancelled: they are the result's shots.
    if (JSON.stringify(told) !== JSON.stringify(result.shots))
      fail(`${basename(sample)} ${run}: the shots told were ${JSON.stringify(told)}, the result's ${JSON.stringify(result.shots)}`);
  }
  checkAudio(sample, video);
}

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

for (const file of rejects) {
  try {
    Open(fileBlob(file));
    fail(`${basename(file)}: opened, but it should have been refused.`);
  } catch (e) {
    console.error(`${basename(file)}: refused: ${e.message}`);
  }
}

console.error(`Loaded assemblies: ${LoadedAssemblies().join(', ')}`);
const byRuns = ffmpeg().filter(name => !atStart.includes(name));
if (byRuns.length > 0) {
  fail(`FFmpeg assemblies loaded by the runs: ${byRuns.join(', ')}`);
} else {
  // The AOT runtime loads some assemblies before the first call that the interpreter never touches, FFmpeg.AutoGen
  // among them (in the bundle because ShotDetector references it); interpreted, an assembly loads when code first
  // needs it, so there "none at all" shows that nothing on this path touches FFmpeg.
  console.error(atStart.length === 0
    ? 'No FFmpeg assembly loaded at all.'
    : `No FFmpeg assembly loaded by the runs (loaded at start: ${atStart.join(', ')}).`);
}
process.exit(failed ? 1 : 0);

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
