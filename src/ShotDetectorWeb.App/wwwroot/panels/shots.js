// The shots, a table under the video and the timeline: a row for each shot as soon as ShotDetector knows it, each row
// playing the video from the shot's start. A stopped run's are its result's, the last ending where it stopped. A run
// lost with its worker (one that didn't answer a stop) keeps its rows, with one more line, marked unfinished, from the
// last cut to the frame the run had reached (the page's own times, not a shot). The shot playing (app.js's 'playing') has
// its row marked, kept in view while the video plays.
import { card, on } from '../app.js';
import { secondsOf, seek, timecode } from '../timeline.js';

const section = card('Shots', {
  place: 'below',
  mark: 'exact',
  markTitle: 'The shots as in the scenes CSV, ShotDetector\'s, checked byte for byte; a lost run\'s unfinished line ' +
    'is the page\'s, as its tooltip says',
});
section.id = 'shots-card';
const table = document.createElement('table');
table.id = 'shots';
table.innerHTML = '<thead><tr><th>Shot</th><th>Start</th><th>End</th><th>Length</th></tr></thead><tbody></tbody>';
section.append(table);
const body = table.tBodies[0];
let last = null; // the last shot told

function addRow(shot) {
  row(body.insertRow(), [shot.shot, shot.startTimecode, shot.endTimecode, `${shot.durationSeconds.toFixed(3)} s`],
    secondsOf(shot.startTimecode));
  last = shot;
}

// The part of the video after the last cut that a lost run had read: the page's own times (frames done over the
// frame rate), not a shot of ShotDetector's, so it says so.
function addUnfinished(from, to) {
  const r = body.insertRow();
  r.className = 'unfinished';
  row(r, ['unfinished', timecode(from), timecode(to), `${(to - from).toFixed(3)} s`], from);
  r.title = `Not a shot: the run was lost here. Play from ${timecode(from)}`;
}

function row(r, texts, at) {
  for (const text of texts) r.insertCell().textContent = text;
  r.tabIndex = 0;
  r.title = `Play from ${texts[1]}`;
  r.onclick = () => seek(at, { play: true });
  r.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); seek(at, { play: true }); } };
}

function clear() {
  body.replaceChildren();
  last = null;
}

on('chose', clear);
on('run', clear);
on('shot', ({ shot }) => addRow(shot));
// The result's shots, which are the ones told while it ran: the table is the result's.
on('done', ({ result }) => {
  clear();
  for (const shot of result.shots) addRow(shot);
});
on('cancelled', ({ video: { frameRate: { num, den } }, done }) => {
  const from = last ? secondsOf(last.endTimecode) : 0, to = done * den / num;
  if (to > from) addUnfinished(from, to);
});

// The row of the shot playing. The table is redrawn on a new run and on the result, so the mark is checked on each
// 'playing' rather than kept.
on('playing', ({ shot, paused }) => {
  const want = shot ? rowOf(shot.shot) : null;
  for (const r of body.querySelectorAll('tr.current')) if (r !== want) r.classList.remove('current');
  if (!want || want.classList.contains('current')) return;
  want.classList.add('current');
  if (!paused) inView(want);
});

// The table's row for a shot: its rows are the shots in order, from 1 (and, after a cancel, one unfinished line).
function rowOf(number) {
  const r = body.rows[number - 1];
  return r && r.cells[0].textContent === String(number) ? r : null;
}

// Scrolls the table's own box, not the page, so the row is in view below its sticky headings.
function inView(r) {
  const head = table.tHead.getBoundingClientRect().bottom;
  const top = r.getBoundingClientRect().top, bottom = r.getBoundingClientRect().bottom;
  const boxBottom = section.getBoundingClientRect().bottom;
  if (top < head) section.scrollTop -= head - top;
  else if (bottom > boxBottom) section.scrollTop += top - head;
}
