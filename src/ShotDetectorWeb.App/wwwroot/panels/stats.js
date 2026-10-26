// The shots' statistics, a card (backlog "Shot statistics and a timeline"): from the shots as Detect returns them, how
// many, their average and median length, the shortest and the longest (a click plays it), cuts per minute and how long
// the run took against the length it read. They are the page's own figures, worked out here from the shots, not
// ShotDetector's, and the card says so. While the video plays the card says which shot is on, and the shots table
// (panels/shots.js, its rows one per shot in order) marks that shot's row and keeps it in view. The current shot is
// known from the shots told while a run goes, so a cancelled run's are marked too; the figures wait for a finished run.
import { card, on } from '../app.js';
import { secondsOf, seek, timecode } from '../timeline.js';

const video = document.getElementById('video');
const table = document.getElementById('shots'); // panels/shots.js's, listed before this one in index.html

const section = card('Shot statistics');
section.id = 'stats';
section.hidden = true;
const tag = document.createElement('span');
tag.className = 'own';
tag.textContent = 'the page\'s own';
tag.title = 'Worked out by the page from the shots ShotDetector found, not ShotDetector\'s numbers';
section.firstElementChild.append(' ', tag);
const list = document.createElement('dl');
const now = document.createElement('p');
now.className = 'now';
const note = document.createElement('p');
note.className = 'note';
note.textContent = 'From the run\'s shots: their lengths as in the scenes CSV, a cut where each shot after the first ' +
  'starts, the length read from the first shot\'s start to the last one\'s end. Exact values in the tooltips.';
section.append(list, now, note);

let shots = [];   // the shots known: told while a run goes, the result's once it is done
let starts = [];  // each shot's start in seconds, and its end
let ends = [];
let current = -1; // the index of the shot the video is in, -1 for none

function clear() {
  shots = [];
  starts = [];
  ends = [];
  section.hidden = true;
  list.replaceChildren();
  follow();
}

function add(shot) {
  shots.push(shot);
  starts.push(secondsOf(shot.startTimecode));
  ends.push(secondsOf(shot.endTimecode));
  follow();
}

on('chose', clear);
on('run', clear);
on('shot', ({ shot }) => add(shot));
on('done', ({ result, seconds }) => {
  clear();
  for (const shot of result.shots) add(shot);
  if (shots.length === 0) return;
  fill(seconds);
  section.hidden = false;
  follow();
});

function fill(seconds) {
  const lengths = shots.map(s => s.durationSeconds);
  const n = lengths.length, total = ends.at(-1) - starts[0];
  const sorted = [...lengths].sort((a, b) => a - b);
  const median = n % 2 === 1 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
  const shortest = lengths.indexOf(sorted[0]), longest = lengths.indexOf(sorted.at(-1));
  const cuts = n - 1;
  const factor = total / seconds;
  for (const [term, value, title] of [
    ['Shots', `${n}`, `${n} ${n === 1 ? 'shot' : 'shots'} in ${total.toFixed(3)} s`],
    ['Average length', length(total / n), `${(total / n).toFixed(6)} s: ${total.toFixed(3)} s over ${n} shots`],
    ['Median length', length(median), `${median.toFixed(6)} s: half the shots are shorter, half longer`],
    ['Shortest', which(shortest), `Shot ${shots[shortest].shot}, ${lengths[shortest].toFixed(3)} s. Play it`],
    ['Longest', which(longest), `Shot ${shots[longest].shot}, ${lengths[longest].toFixed(3)} s. Play it`],
    ['Cuts per minute', total > 0 ? (cuts / total * 60).toFixed(1) : '–',
      `${cuts} ${cuts === 1 ? 'cut' : 'cuts'} in ${(total / 60).toFixed(3)} min`],
    ['Run', `${factor.toFixed(1)} times real time`,
      `${seconds.toFixed(1)} s for ${total.toFixed(1)} s of video${factor >= 1 ? '' : ': slower than playing it'}`],
  ]) {
    const dt = document.createElement('dt'), dd = document.createElement('dd');
    dt.textContent = term;
    if (typeof value === 'string') dd.textContent = value; else dd.append(value);
    dd.title = title;
    list.append(dt, dd);
  }
}

const length = s => `${s.toFixed(2)} s`;

// A shot's length and number, a button that plays it.
function which(i) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'shot-link';
  b.textContent = `${length(shots[i].durationSeconds)} (shot ${shots[i].shot})`;
  b.onclick = () => seek(starts[i], { play: true });
  return b;
}

// The shot the video is in: the last one starting at or before the playhead, if the playhead is before its end.
function shotAt(t) {
  let lo = 0, hi = starts.length - 1, found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (starts[mid] <= t + 1e-6) { found = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return found >= 0 && t < ends[found] - 1e-6 ? found : -1;
}

// The card's line and the table's mark follow the video. The table is redrawn by its own panel (a new run, the result),
// so the mark is checked on each update rather than kept.
function follow() {
  current = shotAt(video.currentTime || 0);
  now.title = '';
  if (current < 0) {
    now.textContent = shots.length > 0 ? 'Now: outside the shots found.' : '';
  } else {
    const into = (video.currentTime || 0) - starts[current], s = shots[current];
    now.textContent = `Now: shot ${s.shot} of ${shots.length}, ${into.toFixed(1)} s into ${s.durationSeconds.toFixed(1)} s.`;
    now.title = `Shot ${s.shot}: ${s.startTimecode} to ${s.endTimecode}; the playhead at ${timecode(video.currentTime || 0)}`;
  }
  mark();
}

function mark() {
  const want = current >= 0 ? row(shots[current].shot) : null;
  for (const r of table.querySelectorAll('tr.current')) if (r !== want) r.classList.remove('current');
  if (!want || want.classList.contains('current')) return;
  want.classList.add('current');
  if (!video.paused) inView(want);
}

// The table's row for a shot: its rows are the shots in order, from 1 (and, after a cancel, one unfinished line).
function row(number) {
  const r = table.tBodies[0].rows[number - 1];
  return r && r.cells[0].textContent === String(number) ? r : null;
}

// Scrolls the table's own box, not the page, so the row is in view below its sticky headings.
function inView(r) {
  const box = table.parentElement, head = table.tHead.getBoundingClientRect().bottom;
  const top = r.getBoundingClientRect().top, bottom = r.getBoundingClientRect().bottom;
  const boxBottom = box.getBoundingClientRect().bottom;
  if (top < head) box.scrollTop -= head - top;
  else if (bottom > boxBottom) box.scrollTop += top - head;
}

for (const type of ['timeupdate', 'seeking', 'emptied']) video.addEventListener(type, follow);
