// The App's exports under Node, as the page's worker calls them (src/ShotDetectorWeb.App/wwwroot/worker.js): each
// sample opened, then every run of tools/Shared/Runs.cs (RUNS below) detected on it, the scene and stats CSVs written to
// the output folder under the names tools/NativeCheck gives its own (tools/check.sh compares the two folders). Each
// file to refuse must be refused when opened. Files go in as the page's do, a Blob read in slices by .NET's BlobStream
// through the published blob-read.js (tools/node-host.mjs). Fails if the runs load an assembly of FFmpeg's; says whether one was
// loaded at all (interpreted: none).
//   node tools/app-check.mjs <the publish's wwwroot> <out dir> <sample.mp4>... [--reject <file>...]
import { mkdirSync, writeFileSync } from 'node:fs';
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
const at = rest.indexOf('--reject');
const samples = at < 0 ? rest : rest.slice(0, at);
const rejects = at < 0 ? [] : rest.slice(at + 1);

let progress = [];
const { dotnet } = await import(pathToFileURL(resolve(wwwroot, '_framework/dotnet.js')).href);
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('host', { ...await host(wwwroot), progress: (done, expected) => progress.push([done, expected]) });
const { Open, Detect, LoadedAssemblies } = (await getAssemblyExports(getConfig().mainAssemblyName)).ShotDetectorWeb.App.Api;
await runMain();

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
    const started = performance.now();
    const result = JSON.parse(Detect(JSON.stringify(options)));
    const seconds = (performance.now() - started) / 1000;
    const name = join(outDir, `${basename(sample, extname(sample))}.${run}`);
    writeFileSync(`${name}.scenes.csv`, result.scenesCsv);
    writeFileSync(`${name}.stats.csv`, result.statsCsv);
    console.error(`${basename(sample)} ${run}: ${result.frameCount} frames, ${result.shots.length} shots, ${seconds.toFixed(2)} s`);
    // The worker turns these into the page's progress bar: they must come, and the last one says every frame is done.
    const last = progress.at(-1);
    if (last?.[0] !== result.frameCount)
      fail(`${basename(sample)} ${run}: progress ended at ${JSON.stringify(last)}, not at ${result.frameCount} frames`);
  }
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
