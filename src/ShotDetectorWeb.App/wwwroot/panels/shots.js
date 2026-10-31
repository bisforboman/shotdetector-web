// The shots, a table under the video and the timeline: a row for each shot as soon as ShotDetector knows it, each row
// playing the video from the shot's start. A stopped run's are its result's, the last ending where it stopped. A run
// lost with its worker (one that didn't answer a stop) keeps its rows, with one more line, marked unfinished, from the
// last cut to the frame the run had reached (the page's own times, not a shot). The shot playing (app.js's 'playing') has
// its row marked, kept in view while the video plays. Each shot's row starts with a still of its middle frame: the
// browser's decode, not ShotDetector's, from a hidden <video> of the panel's own on the same file, taken as rows come
// into the table's view, live shots included, one seek at a time (the owner's choices, 2026-10-29: the player isn't
// moved, and a shot's first frame is often still black from a fade in).
import { card, on } from '../app.js';
import { secondsOf, seek, timecode } from '../timeline.js';

const section = card('Shots', {
  place: 'below',
  mark: 'exact',
  markTitle: 'The shots as in the scenes CSV, ShotDetector\'s, checked byte for byte; the stills are the browser\'s ' +
    'decode and a lost run\'s unfinished line the page\'s, as their tooltips say',
});
section.id = 'shots-card';
const table = document.createElement('table');
table.id = 'shots';
table.innerHTML = '<thead><tr><th class="still" title="Each shot\'s middle frame as this browser decodes it: a ' +
  'picture of the video, not ShotDetector\'s frame">Still</th>' +
  '<th>Shot</th><th>Start</th><th>End</th><th>Length</th></tr></thead><tbody></tbody>';
section.append(table);
const body = table.tBodies[0];
let last = null; // the last shot told

function addRow(shot) {
  const r = body.insertRow();
  stillCell(r.insertCell(), shot);
  row(r, [shot.shot, shot.startTimecode, shot.endTimecode, `${shot.durationSeconds.toFixed(3)} s`],
    secondsOf(shot.startTimecode));
  last = shot;
}

// The part of the video after the last cut that a lost run had read: the page's own times (frames done over the
// frame rate), not a shot of ShotDetector's, so it says so.
function addUnfinished(from, to) {
  const r = body.insertRow();
  r.className = 'unfinished';
  r.insertCell().className = 'still';
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
  seen.disconnect();
  wanted.clear();
}

// The stills. A shot's still is its middle frame, sought half a frame after that frame's start, so that the seek lands
// inside it whichever way the browser rounds. The canvases are kept by that time for the file: a new run and the
// result's table after its live rows find theirs taken (a stopped run's last shot, cut short, has a middle of its own).
const STILL_WIDTH = 128; // the canvas's width in CSS pixels (the cell's at most), drawn at the screen's density
const SEEK_MS = 5000; // a seek that hasn't answered by then gives no still
const source = document.createElement('video');
source.muted = true;
source.preload = 'auto';
// file: which file the stills are of, so that a late seek of the last one is dropped
let url = null, frameSeconds = 0, file = 0;
const stills = new Map(); // the time a still was taken at -> its canvas
const wanted = new Set(); // the cells in view without their still, in the order they came into view
const seen = new IntersectionObserver(entries => {
  for (const e of entries) {
    if (e.isIntersecting) wanted.add(e.target);
    else wanted.delete(e.target);
  }
  take();
}, { root: section, rootMargin: '200px 0px' });
let taking = false;

function stillCell(cell, shot) {
  cell.className = 'still';
  const box = cell.appendChild(document.createElement('div'));
  box.className = 'thumb';
  const middle = (secondsOf(shot.startTimecode) + secondsOf(shot.endTimecode)) / 2;
  const at = frameSeconds ? (Math.floor(middle / frameSeconds) + 0.5) * frameSeconds : middle;
  cell.dataset.at = at;
  const kept = stills.get(at);
  if (kept) box.append(kept);
  else seen.observe(cell);
}

// The cells in view, one seek at a time, until none is left.
async function take() {
  if (taking) return;
  taking = true;
  try {
    for (const cell of wanted) {
      wanted.delete(cell);
      seen.unobserve(cell);
      if (!cell.isConnected) continue;
      const at = Number(cell.dataset.at), of = file;
      const canvas = stills.get(at) ?? await grab(at);
      if (of !== file) continue;
      if (canvas) {
        stills.set(at, canvas);
        cell.firstChild.append(canvas);
      } else cell.title = 'No still: this browser didn\'t show the frame';
    }
  } finally {
    taking = false;
  }
  if (wanted.size) take(); // cells that came into view as the last seek ended
}

// The frame at a time in the hidden video, on a canvas; null when the browser can't show it.
async function grab(at) {
  if (source.readyState < HTMLMediaElement.HAVE_METADATA && !await settled(source, 'loadedmetadata')) return null;
  source.currentTime = at;
  if (!await settled(source, 'seeked') || !source.videoWidth) return null;
  const scale = devicePixelRatio || 1, canvas = document.createElement('canvas');
  canvas.width = Math.round(STILL_WIDTH * scale);
  canvas.height = Math.round(STILL_WIDTH * scale * source.videoHeight / source.videoWidth);
  canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
  return canvas;
}

// Whether the video fired the event before an error, a new file or SEEK_MS.
function settled(target, event) {
  return new Promise(resolve => {
    const types = [event, 'error', 'emptied'];
    const end = e => {
      clearTimeout(timer);
      for (const type of types) target.removeEventListener(type, end);
      resolve(e?.type === event);
    };
    const timer = setTimeout(end, SEEK_MS);
    for (const type of types) target.addEventListener(type, end);
  });
}

on('chose', ({ file: chosen }) => {
  clear();
  stills.clear();
  file++;
  if (url) URL.revokeObjectURL(url);
  source.src = url = URL.createObjectURL(chosen);
});
on('opened', ({ video: { frameRate: { num, den }, width, height } }) => {
  frameSeconds = den / num;
  table.style.setProperty('--still-aspect', `${width} / ${height}`);
});
on('run', clear);
// A shot told again (a resumed run's last, or a split run's first range's last when the ranges merge) replaces the rows
// from its number on.
on('shot', ({ shot }) => {
  while (body.rows.length >= shot.shot) {
    const r = body.rows[body.rows.length - 1];
    seen.unobserve(r.cells[0]);
    wanted.delete(r.cells[0]);
    r.remove();
  }
  addRow(shot);
});
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
  return r && r.cells[1].textContent === String(number) ? r : null;
}

// Scrolls the table's own box, not the page, so the row is in view below its sticky headings.
function inView(r) {
  const head = table.tHead.getBoundingClientRect().bottom;
  const top = r.getBoundingClientRect().top, bottom = r.getBoundingClientRect().bottom;
  const boxBottom = section.getBoundingClientRect().bottom;
  if (top < head) section.scrollTop -= head - top;
  else if (bottom > boxBottom) section.scrollTop += top - head;
}
