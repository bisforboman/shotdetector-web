// The run, a card beside the video: its figures large (how many shots so far while it goes, then the shots, the cuts,
// the length read and the options it ran with), and the downloads, the scene and stats CSVs as scenedetect writes them,
// named after the file. A stopped run's are the result for the part read, and its figures say where it stopped. A run
// lost with its worker (one that didn't answer a stop) says where it had got to and has no downloads (.NET and the
// result went with the worker).
import { card, on } from '../app.js';
import { secondsOf, timecode } from '../timeline.js';

const section = card('This run', {
  place: 'run',
  mark: 'exact',
  markTitle: 'The shots and the CSVs are ShotDetector\'s, written as scenedetect writes them: the checks compare them ' +
    'byte for byte with ShotDetector\'s FFmpeg path on every sample, natively and on WebAssembly',
});
section.id = 'run';
section.insertAdjacentHTML('beforeend', `
  <dl class="figures"></dl>
  <p id="summary"></p>
  <p class="downloads" hidden>
    <a id="scenes-csv" download>Scenes CSV</a>
    <a id="stats-csv" download>Stats CSV</a>
    <span class="note"></span>
  </p>`);
const figures = section.querySelector('.figures'), summary = section.querySelector('#summary');
const downloads = section.querySelector('.downloads'), what = downloads.querySelector('.note');
const links = [section.querySelector('#scenes-csv'), section.querySelector('#stats-csv')];
let shots = [];
let options = null; // the run's, as Api's Detect takes them (null for a default)

const count = n => `${n} ${n === 1 ? 'shot' : 'shots'}`;

// The figures: each a value, what it is under it, and a tooltip.
function show(...figs) {
  figures.replaceChildren(...figs.map(([value, term, title = '']) => {
    const group = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd');
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
    `${minSceneLength == null ? 'default minimum shot' : `minimum shot ${minSceneLength}`}`,
    'The detector, threshold and minimum shot length the run used; a default is ShotDetector\'s own'];
}

function clear() {
  for (const a of links) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
  }
  downloads.hidden = true;
  figures.replaceChildren();
  summary.textContent = '';
  shots = [];
}

on('chose', () => {
  clear();
  options = null;
});
on('run', ({ options: o }) => {
  clear();
  options = o;
  show(['0', 'shots so far'], ran());
  summary.textContent = 'The shots appear as they are found.';
});
on('shot', ({ shot }) => {
  shots.push(shot);
  show([String(shots.length), `${shots.length === 1 ? 'shot' : 'shots'} so far`], ran());
});
on('done', ({ video, result: r, name }) => {
  clear();
  const rate = r.frameRate.num / r.frameRate.den, n = r.shots.length, cuts = Math.max(n - 1, 0);
  const fps = `${Number(rate.toFixed(3))} fps`;
  if (r.stopped) {
    show([String(n), `${n === 1 ? 'shot' : 'shots'}, the last ending where the run stopped`],
      [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [timecode(r.frameCount / rate), `stopped at: frame ${r.frameCount.toLocaleString('en')} of ` +
        `${video.frameCount.toLocaleString('en')} read`, `Stopped at frame ${r.frameCount} of ${video.frameCount}, at ${fps}`],
      ran());
    summary.textContent = `${count(n)} in the ${r.frameCount} frames read. The CSVs are the ones for that part of the video.`;
  } else {
    show([String(n), n === 1 ? 'shot' : 'shots'], [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [r.duration != null ? `${r.duration.toFixed(2)} s` : `${r.frameCount} frames`,
        `${r.frameCount.toLocaleString('en')} frames at ${fps}`, `${count(n)} in ${r.frameCount} frames at ${fps}` +
        `${r.duration != null ? `, ${r.duration} s` : ''}`],
      ran());
  }
  const [scenes, stats] = links;
  link(scenes, r.scenesCsv, `${name}.scenes.csv`);
  link(stats, r.statsCsv, `${name}.stats.csv`);
  what.textContent = `${name}.scenes.csv and ${name}.stats.csv: the shots and each frame's scores, as scenedetect ` +
    `writes them${r.stopped ? ', for the part read' : ''}.`;
  downloads.hidden = false;
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

function link(a, text, name) {
  a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
  a.download = name;
}
