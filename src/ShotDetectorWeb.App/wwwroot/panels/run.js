// The run, a card: how many shots so far while it goes, then the summary and the downloads, the scene and stats CSVs
// as scenedetect writes them, named after the file. A stopped run's are the result for the part read, and say where it
// stopped. A run lost with its worker (one that didn't answer a stop) says where it had got to and has no downloads
// (.NET and the result went with the worker).
import { card, on } from '../app.js';
import { secondsOf, timecode } from '../timeline.js';

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
on('done', ({ video, result: r, name }) => {
  clear();
  const rate = r.frameRate.num / r.frameRate.den;
  summary.textContent = r.stopped
    ? `Stopped at ${timecode(r.frameCount / rate)}, frame ${r.frameCount} of ${video.frameCount}: ` +
      `${count(r.shots.length)} in the ${r.frameCount} frames read, the last one ending where the run stopped. ` +
      'The CSVs are the ones for that part of the video.'
    : `${count(r.shots.length)} in ${r.frameCount} frames at ${Number(rate.toFixed(3))} fps` +
      `${r.duration != null ? `, ${r.duration.toFixed(2)} s` : ''}.`;
  const [scenes, stats] = links;
  link(scenes, r.scenesCsv, `${name}.scenes.csv`);
  link(stats, r.statsCsv, `${name}.stats.csv`);
  downloads.hidden = false;
});
on('cancelled', ({ video: { frameRate: { num, den } }, done, expected }) => {
  const from = shots.length > 0 ? secondsOf(shots.at(-1).endTimecode) : 0, to = done * den / num;
  summary.textContent = `Lost at frame ${done}${expected > 0 ? ` of ${expected}` : ''}: ` +
    `${count(shots.length)} found${to > from ? ', then one unfinished up to where the run had got to' : ''}. ` +
    'The worker didn\'t answer the stop, so there are no downloads.';
});

function link(a, text, name) {
  a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
  a.download = name;
}
