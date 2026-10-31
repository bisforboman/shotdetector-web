// The run, a card beside the video: its figures large (how many shots so far while it goes, then the shots, the cuts,
// the length read and the options it ran with), and the downloads, the scene and stats CSVs as scenedetect writes them
// and the shots as ShotDetector's JSON and scenedetect's HTML page (Api's ShotsJson and ShotsHtml, asked for here once
// the run is done), named after the file; and for editors, the shots in scenedetect's timeline formats (Api's
// TimelineFile: an EDL, FCPXML, FCP7 XML, OTIO, an x264 QP file and Aegisub keyframes), which name the file at the root, as the browser
// doesn't know its folder: the editor relinks it. A stopped run's are the result for the part read, and its figures say
// where it stopped. A run lost with its worker (one that didn't answer a stop) says where it had got to and has no downloads
// (.NET and the result went with the worker).
import { call, card, on } from '../app.js';
import { secondsOf, timecode } from '../timeline.js';

const section = card('This run', {
  place: 'run',
  mark: 'exact',
  markTitle: 'The shots, the CSVs, the JSON, the HTML and the editors\' files are ShotDetector\'s, the CSVs, the HTML ' +
    'and the editors\' files written as scenedetect writes them: the checks compare them byte for byte with ' +
    'ShotDetector\'s FFmpeg path on every sample, natively and on WebAssembly',
});
section.id = 'run';
section.insertAdjacentHTML('beforeend', `
  <dl class="figures"></dl>
  <p id="summary"></p>
  <p class="downloads" hidden>
    <a id="scenes-csv" download>Scenes CSV</a>
    <a id="stats-csv" download>Stats CSV</a>
    <a id="shots-json" download>Shots JSON</a>
    <a id="shots-html" download>Shots HTML</a>
  </p>
  <p class="downloads editors" hidden>
    <span>For editors:</span>
    <a id="timeline-edl" download>EDL</a>
    <a id="timeline-fcpxml" download>FCPXML</a>
    <a id="timeline-fcp7" download>FCP7 XML</a>
    <a id="timeline-otio" download>OTIO</a>
    <a id="timeline-qp" download>QP</a>
    <a id="timeline-keyframes" download>Keyframes</a>
  </p>`);
const figures = section.querySelector('.figures'), summary = section.querySelector('#summary');
const downloads = section.querySelector('.downloads'), editors = section.querySelector('.editors');
const links = ['#scenes-csv', '#stats-csv', '#shots-json', '#shots-html'].map(id => section.querySelector(id));
// The editors' files: Api's TimelineFile format, the link, the file's extension, its type and what reads it.
const TIMELINES = [
  ['edl', 'timeline-edl', 'edl', 'text/plain', 'a CMX 3600 edit decision list, a cut a shot (DaVinci Resolve, Avid and ' +
    'most editors import it as a timeline; its events name reel AX, not the file)'],
  ['fcpx', 'timeline-fcpxml', 'fcpxml', 'application/xml', 'Final Cut Pro X XML (FCPXML 1.9), a clip a shot (Final Cut ' +
    'Pro, DaVinci Resolve)'],
  ['fcp7', 'timeline-fcp7', 'fcp7.xml', 'application/xml', 'Final Cut Pro 7 XML (xmeml), a clip a shot (Premiere Pro, ' +
    'DaVinci Resolve)'],
  ['otio', 'timeline-otio', 'otio', 'application/json', 'an OpenTimelineIO timeline, a clip a shot (DaVinci Resolve ' +
    '18.5 and later, OpenTimelineIO\'s tools)'],
  ['qp', 'timeline-qp', 'qp', 'text/plain', 'an x264/x265 QP file, a keyframe at each cut, to encode the video so each ' +
    'shot starts on one'],
  ['keyframes', 'timeline-keyframes', 'keyframes.txt', 'text/plain', 'Aegisub\'s keyframes (format v1), frame 0 and each ' +
    'cut, for timing subtitles to the cuts'],
].map(([format, id, extension, type, what]) => ({ format, link: section.querySelector(`#${id}`), extension, type, what }));
const RELINK = 'It names the file at the root, as the browser doesn\'t know its folder: relink it in the editor.';
let shots = [];
let options = null; // the run's, as Api's Detect takes them (null for a default)
let runs = 0; // counts the runs and files chosen, so a result asked for after one is done isn't shown under the next
let fileName = ''; // the file chosen's, which the editors' files name

const count = n => `${n} ${n === 1 ? 'shot' : 'shots'}`;

// The figures: each a value, what it is under it, a tooltip, and a class (the detector's: smaller than the numbers).
function show(...figs) {
  figures.replaceChildren(...figs.map(([value, term, title = '', kind = '']) => {
    const group = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd');
    if (kind) group.className = kind;
    dt.textContent = term;
    dd.textContent = value;
    group.title = title;
    group.append(dt, dd);
    return group;
  }));
}

// The detector, and its threshold and minimum shot length, as the run was told them.
function ran() {
  const { detector, threshold, minSceneLength } = options;
  return [detector, `${threshold == null ? 'default threshold' : `threshold ${threshold}`} · ` +
    `${minSceneLength == null ? 'default minimum shot length' : `minimum shot length ${minSceneLength}`}`,
    'The detector, threshold and minimum shot length the run used; a default is ShotDetector\'s own', 'detector'];
}

function clear() {
  for (const a of [...links, ...TIMELINES.map(t => t.link)]) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
    a.hidden = false;
  }
  downloads.hidden = editors.hidden = true;
  figures.replaceChildren();
  summary.textContent = '';
  shots = [];
}

on('chose', ({ file }) => {
  runs++;
  fileName = file.name;
  clear();
  options = null;
});
on('run', ({ options: o, ranges }) => {
  runs++;
  clear();
  options = o;
  show(['0', 'shots so far'], ran());
  // A run split across workers decides its shots when their ranges merge (decision 2026-10-26-workers.md).
  summary.textContent = ranges
    ? `The shots come once the ${ranges.length} workers' ranges merge.`
    : 'The shots appear as they are found.';
});
on('shot', ({ shot }) => {
  shots.push(shot);
  show([String(shots.length), `${shots.length === 1 ? 'shot' : 'shots'} so far`], ran());
});
on('done', async ({ video, result: r, name }) => {
  clear();
  const rate = r.frameRate.num / r.frameRate.den, n = r.shots.length, cuts = Math.max(n - 1, 0);
  const fps = `${Number(rate.toFixed(3))} fps`;
  if (r.stopped) {
    show([String(n), `${n === 1 ? 'shot' : 'shots'}, the last ending where the run stopped`],
      [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [timecode(r.frameCount / rate), `stopped at: frame ${r.frameCount.toLocaleString('en')} of ` +
        `${video.frames.toLocaleString('en')} read`, `Stopped at frame ${r.frameCount} of ${video.frames}, at ${fps}`],
      ran());
    summary.textContent = `${count(n)} in the ${r.frameCount} frames read. The CSVs are the ones for that part of the video.`;
  } else {
    show([String(n), n === 1 ? 'shot' : 'shots'], [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [r.duration != null ? `${r.duration.toFixed(2)} s` : `${r.frameCount} frames`,
        `${r.frameCount.toLocaleString('en')} frames at ${fps}`, `${count(n)} in ${r.frameCount} frames at ${fps}` +
        `${r.duration != null ? `, ${r.duration} s` : ''}`],
      ran());
  }
  // The shots as JSON and HTML, of the run just done (the worker keeps its result until the next run).
  const at = runs, json = await call('ShotsJson'), html = await call('ShotsHtml');
  if (at !== runs) return;
  const [scenes, stats, shotsJson, shotsHtml] = links;
  const part = r.stopped ? ', for the part read' : '';
  link(scenes, r.scenesCsv, `${name}.scenes.csv`, 'text/csv', `The shots, as scenedetect writes them${part}`);
  link(stats, r.statsCsv, `${name}.stats.csv`, 'text/csv', `Each frame's scores, as scenedetect writes them${part}`);
  link(shotsJson, json, `${name}.shots.json`, 'application/json', `The shots as ShotDetector's JSON${part}`);
  link(shotsHtml, html, `${name}.shots.html`, 'text/html', `The shots as scenedetect's HTML page${part}`);
  downloads.hidden = false;
  // The editors' files, of the same run. One that can't be written (FCPXML and FCP7 XML of no shots) isn't offered.
  const files = await Promise.all(TIMELINES.map(t => call('TimelineFile', t.format, fileName).catch(() => null)));
  if (at !== runs) return;
  TIMELINES.forEach((t, i) => {
    t.link.hidden = files[i] === null;
    if (files[i] === null) return;
    link(t.link, files[i], `${name}.${t.extension}`, t.type, `The shots as ${t.what}, as scenedetect writes it${part}.` +
      `${['fcpx', 'fcp7', 'otio'].includes(t.format) ? ` ${RELINK}` : ''}`);
  });
  editors.hidden = files.every(f => f === null);
});
on('cancelled', ({ video: { frameRate: { num, den } }, done, expected }) => {
  const from = shots.length > 0 ? secondsOf(shots.at(-1).endTimecode) : 0, to = done * den / num;
  show([String(shots.length), `${shots.length === 1 ? 'shot' : 'shots'} found`],
    [timecode(to), `lost at: frame ${done.toLocaleString('en')}${expected > 0 ? ` of ${expected.toLocaleString('en')}` : ''}`],
    ran());
  summary.textContent = `Lost at frame ${done}${expected > 0 ? ` of ${expected}` : ''}: ` +
    `${count(shots.length)} found${to > from ? ', then one unfinished up to where the run had got to' : ''}. ` +
    'The worker didn\'t answer the stop, so there are no downloads.';
});

function link(a, text, name, type, title) {
  a.href = URL.createObjectURL(new Blob([text], { type }));
  a.download = name;
  a.title = `${name}: ${title}`;
}
