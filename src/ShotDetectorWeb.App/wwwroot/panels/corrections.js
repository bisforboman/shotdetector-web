// Correcting the cuts (issue #90, decision 2026-11-02-correcting-cuts.md), a card beside the video under the run's: once
// a run is done or stopped, remove the cut that starts the shot playing, add one at the playhead, move it a frame
// earlier or later, undo and redo, or go back to the detected cuts. The keys do the same: Delete (or Backspace) removes,
// C adds, `,` and `.` move it a frame, Ctrl+Z undoes, Ctrl+Shift+Z or Ctrl+Y redoes. A moved cut takes the playhead
// with it, to its first frame. app.js's correct() makes the result: ShotDetector's LoadScenes of the corrected list,
// which every download then comes from (the run card), and 'corrected' tells the panels. The corrections are the
// viewer's, so the card is marked as the page's; the undo history lasts as long as the run is shown.
import { card, correct, correctedCuts, detectedCuts, frameSeconds, on } from '../app.js';
import { playheadTime, seek } from '../timeline.js';

const section = card('Corrections', {
  place: 'run',
  mark: 'page',
  markTitle: 'The cuts as you corrected them on the page, not the detector\'s. The downloads are then made from the ' +
    'corrected list by ShotDetector (LoadScenes), as scenedetect\'s load-scenes would make them: the checks compare ' +
    'them byte for byte with ShotDetector\'s FFmpeg path for the same list',
});
section.id = 'corrections';
section.hidden = true;
section.dataset.pending = '0';
section.insertAdjacentHTML('beforeend', `
  <p class="edit-buttons">
    <button type="button" id="cut-remove" title="Remove the cut that starts the shot playing (Delete)">Remove cut</button>
    <button type="button" id="cut-add" title="Add a cut at the playhead's frame (C)">Add cut</button>
    <button type="button" id="cut-earlier" title="Move the cut that starts the shot playing a frame earlier (,)">◂ Frame</button>
    <button type="button" id="cut-later" title="Move the cut that starts the shot playing a frame later (.)">Frame ▸</button>
    <button type="button" id="cut-undo" title="Undo (Ctrl+Z)">Undo</button>
    <button type="button" id="cut-redo" title="Redo (Ctrl+Shift+Z)">Redo</button>
    <button type="button" id="cut-revert" class="quiet" title="Back to the cuts as detected">Detected cuts</button>
  </p>
  <p id="corrections-summary"></p>`);
const $ = id => section.querySelector(`#${id}`);
const removeButton = $('cut-remove'), addButton = $('cut-add'), earlier = $('cut-earlier'), later = $('cut-later');
const undoButton = $('cut-undo'), redoButton = $('cut-redo'), revertButton = $('cut-revert');
const summary = $('corrections-summary');

let video = null;  // the run's video (Open's), for where its frames are
let frames = 0;    // the frames the run read: a cut is from 1 to frames - 1
let cuts = [];     // the cuts as corrected now (asked for: the last list sent to correct())
let undo = [];     // the cut lists before each correction, the last last
let redo = [];
let pending = 0;   // corrections sent and not yet made

function clear() {
  section.hidden = true;
  video = null;
  cuts = [];
  undo = [];
  redo = [];
}

on('chose', clear);
on('run', clear);
on('cancelled', clear);
on('done', ({ video: v, result }) => {
  video = v;
  frames = result.frameCount;
  cuts = detectedCuts();
  undo = [];
  redo = [];
  section.hidden = false;
  tell(null);
});
// The page's own corrections (a kept run's, applied again) set the list; the card's own are in it already, and one
// made while later ones are on their way would set it back.
on('corrected', ({ edits }) => {
  if (pending === 0) cuts = correctedCuts();
  tell(edits);
});

// The card's data-pending: corrections on their way (the page check waits for none).
function send(list) {
  section.dataset.pending = ++pending;
  correct(list).finally(() => { section.dataset.pending = --pending; });
}

function tell(edits) {
  const removed = edits?.removed.length ?? 0, added = edits?.added.length ?? 0;
  summary.textContent = removed + added === 0
    ? 'The cuts as detected. Correct them here or with the keys; the downloads follow.'
    : `${removed} detected ${removed === 1 ? 'cut' : 'cuts'} removed, ${added} added (a moved cut is both): the ` +
      'downloads are the corrected list, made by ShotDetector (LoadScenes); the stats CSV stays the run\'s.';
  summary.title = 'Keys: Delete removes the cut that starts the shot playing, C adds one at the playhead, , and . ' +
    'move it a frame, Ctrl+Z undoes, Ctrl+Shift+Z redoes';
  buttons();
}

function buttons() {
  const none = cutAt() === null;
  removeButton.disabled = earlier.disabled = later.disabled = none;
  addButton.disabled = !addable(frameAt());
  undoButton.disabled = undo.length === 0;
  redoButton.disabled = redo.length === 0;
  revertButton.disabled = same(cuts, detectedCuts());
}

const same = (a, b) => a.length === b.length && a.every((c, i) => c === b[i]);

// The frame at the playhead: the last one starting at or before it.
function frameAt(t = playheadTime()) {
  let lo = 0, hi = frames - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (frameSeconds(video, mid) <= t + 1e-6) lo = mid; else hi = mid - 1;
  }
  return lo;
}

// The cut that starts the shot playing: the last at or before the playhead's frame, or null before the first.
function cutAt() {
  if (!video) return null;
  const f = frameAt();
  let found = null;
  for (const c of cuts) if (c <= f) found = c;
  return found;
}

const addable = f => video !== null && f > 0 && f < frames && !cuts.includes(f);

// Seeks to a frame's middle, so the picture shown is that frame and the playhead's frame is it.
function show(f) {
  seek((frameSeconds(video, f) + frameSeconds(video, f + 1)) / 2);
}

function change(next, { to = null } = {}) {
  next = [...new Set(next)].sort((a, b) => a - b);
  if (same(next, cuts)) return;
  undo.push(cuts);
  redo = [];
  cuts = next;
  if (to !== null) show(to);
  buttons();
  send(next);
}

function remove() {
  const c = cutAt();
  if (c !== null) change(cuts.filter(x => x !== c));
}

function add() {
  const f = frameAt();
  if (addable(f)) change([...cuts, f]);
}

// Moves the cut that starts the shot playing by a frame, not past its neighbours (two cuts on one frame are one).
function move(by) {
  const c = cutAt();
  if (c === null) return;
  const to = c + by;
  if (!addable(to)) return;
  change([...cuts.filter(x => x !== c), to], { to });
}

function step(from, to) {
  if (from.length === 0) return;
  to.push(cuts);
  cuts = from.pop();
  buttons();
  send(cuts);
}

removeButton.onclick = remove;
addButton.onclick = add;
earlier.onclick = () => move(-1);
later.onclick = () => move(1);
undoButton.onclick = () => step(undo, redo);
redoButton.onclick = () => step(redo, undo);
revertButton.onclick = () => change(detectedCuts());

// The keys, while a run is shown and nothing that takes typing has the focus.
document.addEventListener('keydown', e => {
  if (section.hidden || e.altKey || e.target.closest?.('input, textarea, select, [contenteditable]')) return;
  const ctrl = e.ctrlKey || e.metaKey, key = e.key.toLowerCase();
  const act = ctrl
    ? key === 'z' && !e.shiftKey ? () => step(undo, redo)
      : (key === 'z' && e.shiftKey) || key === 'y' ? () => step(redo, undo) : null
    : e.key === 'Delete' || e.key === 'Backspace' ? remove
      : key === 'c' ? add : e.key === ',' ? () => move(-1) : e.key === '.' ? () => move(1) : null;
  if (!act) return;
  e.preventDefault();
  act();
});

// The buttons follow the playhead: which cut starts the shot playing, whether a cut can go where it is.
on('playing', () => { if (video) buttons(); });
