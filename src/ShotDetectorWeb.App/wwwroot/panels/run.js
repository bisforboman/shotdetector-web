// The run, a card: how many shots so far while it goes, then the summary and the downloads, the scene and stats CSVs
// as scenedetect writes them, named after the file. A cancelled run says where it stopped and has no downloads (.NET
// and the result went with the worker).
import { card, on } from '../app.js';
import { secondsOf } from '../timeline.js';

const section = card('This run');
section.id = 'run';
section.insertAdjacentHTML('beforeend', `
  <p id="summary"></p>
  <p class="downloads" hidden>
    <a id="scenes-csv" download>Scenes CSV</a>
    <a id="stats-csv" download>Stats CSV</a>
  </p>`);
const summary = section.querySelector('#summary'), downloads = section.querySelector('.downloads');
const links = [section.querySelector('#scenes-csv'), section.querySelector('#stats-csv')];
let shots = [];

const count = n => `${n} ${n === 1 ? 'shot' : 'shots'}`;

function clear() {
  for (const a of links) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
  }
  downloads.hidden = true;
  summary.textContent = '';
  shots = [];
}

on('chose', clear);
on('run', () => {
  clear();
  summary.textContent = 'The shots appear as they are found.';
});
on('shot', ({ shot }) => {
  shots.push(shot);
  summary.textContent = `${count(shots.length)} so far.`;
});
on('done', ({ result: r, name }) => {
  clear();
  const rate = r.frameRate.num / r.frameRate.den;
  summary.textContent = `${count(r.shots.length)} in ${r.frameCount} frames at ${Number(rate.toFixed(3))} fps` +
    `${r.duration != null ? `, ${r.duration.toFixed(2)} s` : ''}.`;
  const [scenes, stats] = links;
  link(scenes, r.scenesCsv, `${name}.scenes.csv`);
  link(stats, r.statsCsv, `${name}.stats.csv`);
  downloads.hidden = false;
});
on('cancelled', ({ video: { frameRate: { num, den } }, done, expected }) => {
  const from = shots.length > 0 ? secondsOf(shots.at(-1).endTimecode) : 0, to = done * den / num;
  summary.textContent = `Cancelled at frame ${done}${expected > 0 ? ` of ${expected}` : ''}: ` +
    `${count(shots.length)} found${to > from ? ', then one unfinished up to where the run stopped' : ''}. ` +
    'No downloads for a cancelled run.';
});

function link(a, text, name) {
  a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
  a.download = name;
}
