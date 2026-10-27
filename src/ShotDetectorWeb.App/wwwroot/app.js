// The page: picks a file, has the worker (worker.js, which owns the .NET runtime) open it and detect, and tells the
// panels what happens. How it is built: docs/decisions/2026-10-24-the-page.md and 2026-10-25-one-page.md. This file
// keeps the page's states (idle, opening, detecting, stopping, done, stopped, lost, error), the landing that folds into
// a toolbar once a file is picked, the one-line status, the options, the estimate, stop, the demo and the shot playing
// under the video (decision 2026-10-26-new-ui.md); what else is shown of a file or a run is in the panels
// (panels/<name>.js, listed in index.html), each a lane of the timeline (timeline.js) or a card. A panel imports from
// here:
//   on(event, f)       f(data) on each of the page's events, in this order for a run:
//                        'chose'    { file }                     a new file: whatever was shown of the last one goes
//                        'opened'   { video }                    the file's YuvVideo, from Api's Open
//                        'run'      { video, options }           a run starts: whatever was shown of the last run goes
//                        'progress' { video, done, expected }    frames done and expected (0 when unknown)
//                        'shot'     { video, shot }              a shot as soon as ShotDetector knows it
//                        'done'     { video, result, seconds, name }   the run's result (Api's Detect, its CSVs as
//                                                                scenesCsv and statsCsv); name: the file's, without its
//                                                                extension, for downloads. A stopped run's too, with
//                                                                result.stopped: the result for its frameCount frames
//                        'cancelled' { video, done, expected }   the run was lost at frame done: the worker didn't
//                                                                answer a stop and was terminated
//                        'audio'    { video, audio }             the file's sound, after its first finished run: Api's
//                                                                Audio (CLAUDE.md "Layout" has its JSON), once per
//                                                                opened file; a failure as notMeasured
//                        'playing'  { shot, paused }             whenever the playhead moves or the shots change: the
//                                                                shot it is in (null outside the shots known), and
//                                                                whether the video is paused
//   call(name, ...args) an export of Api's, by name, in the worker: a promise of what it returns
//   card(title, { place, mark, markTitle }) a card for the panel to fill, in its place: 'run' beside the video (the run
//                       and what goes with it), 'below' under the timeline, full width (the shots), 'details' (the
//                       default) in the grid of cards last. mark says whose numbers the card shows, a chip on its
//                       title (MARKS), markTitle how they are checked, the chip's tooltip; a row of another kind says
//                       so in its own tooltip.
import { secondsOf, setDuration, timecode } from './timeline.js';

// The estimate before a run: frames per second scale with picture size (pixels per second about constant). Until this
// browser has finished a run (its own rate, kept in localStorage), the rate is the one measured for Sintel's trailer
// (854x480, the AOT build Pages serves, headless Chromium on a 4-core cloud VM, 2026-10-25): REFERENCE_FPS frames per
// second at 854x480.
const REFERENCE_FPS = 46;
const REFERENCE_PIXELS_PER_SECOND = REFERENCE_FPS * 854 * 480;
const RATE_KEY = 'pixelsPerSecond';

// ShotDetector's default threshold for each detector (DetectionOptions.Threshold), prefilled when one is picked. A
// threshold left at its default is sent as none, so the run is the library's own default run.
const DEFAULT_THRESHOLDS = { adaptive: 3, content: 27, threshold: 12, histogram: 0.05, hash: 0.395 };
const DEFAULT_MIN_LENGTH = '0.6s';

const $ = id => document.getElementById(id);
const form = $('form'), fileInput = $('file'), drop = $('drop'), dropText = $('drop-text');
const detector = $('detector'), threshold = $('threshold'), minLength = $('min-length');
const detectButton = $('detect'), stopButton = $('stop'), demoButton = $('demo'), changeButton = $('change');
const fileName = $('file-name'), fileFacts = $('file-facts');
const progress = $('progress'), statusText = $('status-text'), error = $('error');
const result = $('result'), video = $('video'), now = $('now');
const places = { run: $('run-cards'), below: $('below'), details: $('details') };

// Whose number it is (decision 2026-10-26-new-ui.md, "One mark"): the CSVs, checked byte for byte; the core's measures,
// checked against FFmpeg within a tolerance; the core's description of the file, checked field by field; and what the
// page works out for display. The footer says what the words mean.
const MARKS = { exact: 'scenedetect\'s, byte for byte', measured: 'measured', core: 'the core\'s', page: 'the page\'s' };

let file = null;        // the chosen file
let opened = null;      // the file's YuvVideo, once the current worker has it open
let estimate = '';      // the estimate of the current run, as the status's tooltip shows it
let estimated = 0;      // and its seconds
let state = 'idle';
let started = 0;        // when the current detection started (performance.now())
let framesDone = 0;     // frames the current run has done, as its last progress report said
let framesExpected = 0; // and how many it expects (0 when unknown)
let measured = null;    // the YuvVideo whose sound the panels have been told (Audio once per opened file)
let running = null;     // the current run's Detect call (a promise), while it goes
let shots = [];         // the shots known, for the caption: told while a run goes, the result's once it is done
let starts = [];        // each one's start in seconds, and its end
let ends = [];

const listeners = {};
export function on(event, f) {
  (listeners[event] ??= []).push(f);
}

function emit(event, data) {
  for (const f of listeners[event] ?? []) f(data);
}

export function card(title, { place = 'details', mark, markTitle = '' } = {}) {
  const section = document.createElement('section');
  section.className = 'card';
  const heading = document.createElement('h2');
  heading.textContent = title;
  if (mark) {
    const chip = document.createElement('span');
    chip.className = `chip ${mark}`;
    chip.textContent = MARKS[mark];
    chip.title = markTitle;
    heading.append(' ', chip);
  }
  section.append(heading);
  places[place].append(section);
  return section;
}

// The worker and its calls: each call has an id, and the worker answers them in order. A worker that is replaced (one
// that didn't answer a stop, a crash) takes its calls with it, and a call the worker cancelled ends the same way: they
// reject with an AbortError, which the page doesn't show.
let worker, calls = new Map(), lastId = 0;

export function call(name, ...args) {
  return new Promise((resolve, reject) => {
    const id = ++lastId;
    calls.set(id, { resolve, reject });
    worker.postMessage({ type: 'call', id, name, args });
  });
}

function newWorker() {
  const w = new Worker('worker.js', { type: 'module' });
  // Only the current worker's: one terminated may still have messages on their way.
  w.onmessage = ({ data }) => { if (w === worker) fromWorker[data.type]?.(data); };
  // Not an exception from .NET (those answer a call as 'error'): the worker itself failed, e.g. out of memory.
  w.onerror = e => {
    e.preventDefault();
    replaceWorker();
    fail(`The worker stopped: ${e.message || 'unknown error'}`);
  };
  return w;
}

function replaceWorker() {
  worker?.terminate();
  for (const { reject } of calls.values()) reject(new DOMException('The worker was replaced.', 'AbortError'));
  calls = new Map();
  worker = newWorker();
  opened = null;
}

function answer(id) {
  const c = calls.get(id);
  calls.delete(id);
  return c;
}

const fromWorker = {
  result({ id, value }) { answer(id)?.resolve(value); },
  error({ id, message }) { answer(id)?.reject(new Error(message)); },
  cancelled({ id }) { answer(id)?.reject(new DOMException('The call was cancelled.', 'AbortError')); },
  progress({ done, expected }) {
    if (!runGoes()) return;
    framesDone = done;
    framesExpected = expected;
    if (expected > 0) progress.value = Math.min(done / expected, 1);
    const seconds = (performance.now() - started) / 1000;
    const fps = seconds > 0 ? done / seconds : 0;
    if (state === 'stopping') say(`Stopping at frame ${count(done)}…`);
    else say(`Frame ${count(done)}${expected > 0 ? ` of ${count(expected)}` : ''} · ${fps.toFixed(0)} frames per second` +
      `${expected > 0 ? ` · about ${duration(left(done, expected, seconds, fps))} left` : ''}`,
      `${estimate} The time left: at this run's rate so far, after its first ${LEFT_AFTER} seconds.`);
    emit('progress', { video: opened, done, expected });
  },
  // A shot as soon as ShotDetector knows it: a few frames after the cut that ends it with one detector (what the page
  // sends), the same object as in the result's shots (tools/app-check.mjs checks they are the same).
  shot({ shot }) {
    if (!runGoes()) return;
    emit('shot', { video: opened, shot });
    addShot(shot);
  },
};

const runGoes = () => state === 'detecting' || state === 'stopping';

// The states: idle (a file picked or not), opening, detecting, stopping (told to stop, its result to come), done,
// stopped (a stopped run's result, shown as a finished one's), lost (a run whose worker didn't answer a stop) and error.
function setState(next) {
  state = next;
  const busy = next === 'opening' || runGoes();
  document.body.dataset.state = next;
  // While a run goes, another file can be chosen: the run is abandoned (choose).
  fileInput.disabled = changeButton.disabled = next === 'opening';
  for (const input of [detector, threshold, minLength]) input.disabled = busy;
  detectButton.disabled = busy || !file;
  demoButton.disabled = busy;
  detectButton.hidden = busy;
  // Opening can't be stopped half-way, only abandoned: the worker is terminated (stop).
  stopButton.hidden = !busy;
  stopButton.disabled = next === 'stopping';
  stopButton.textContent = next === 'opening' ? 'Cancel' : 'Stop';
  progress.hidden = !busy;
  error.hidden = next !== 'error';
  if (next === 'opening') result.hidden = true;
}

// The status line, and the tooltip that says where its figures come from.
function say(text, title = '') {
  statusText.textContent = text;
  statusText.title = title;
}

// A new file. During a run, the run is abandoned first: cancelled (Api's Cancel through the worker's 'cancel'), so it
// keeps nothing and the worker stays, or the worker replaced if it doesn't answer.
async function choose(chosen) {
  if (!chosen || state === 'opening') return;
  if (running) {
    const run = running;
    running = null;
    worker.postMessage({ type: 'cancel' });
    if (!await answered(run)) replaceWorker();
  }
  opened = null;
  result.hidden = true;
  setDuration(0);
  emit('chose', { file: chosen });
  clearShots();
  dropText.textContent = `${chosen.name} (${megabytes(chosen.size)})`;
  fileName.textContent = chosen.name;
  fileFacts.textContent = megabytes(chosen.size);
  // The landing folds into the toolbar.
  document.body.classList.add('picked');
  file = chosen;
  if (video.src) URL.revokeObjectURL(video.src);
  video.src = URL.createObjectURL(file);
  say('');
  setState('idle');
}

// Opens the file if the current worker hasn't, then detects. Errors are the library's messages; a call that a cancel or
// a replaced worker took away is no error.
async function start() {
  if (!file) return;
  error.textContent = '';
  try {
    if (!opened) {
      setState('opening');
      progress.removeAttribute('value');
      say(`Opening ${file.name}…`);
      // The File itself: the worker reads it in slices as .NET needs them, so it is never copied whole (worker.js).
      opened = JSON.parse(await call('Open', file));
      setDuration(lengthOf(opened));
      fileFacts.textContent = facts(file, opened);
      emit('opened', { video: opened });
    }
    await detect();
  } catch (e) {
    if (e.name !== 'AbortError') fail(e.message);
  }
}

// One run on the opened file. Stop (stop) ends it early with the result for the frames read, which is shown as a
// finished run's is; a new file (choose) abandons it.
async function detect() {
  setState('detecting');
  framesDone = framesExpected = 0;
  const run = options();
  emit('run', { video: opened, options: run });
  clearShots();
  result.hidden = false;
  progress.removeAttribute('value');
  estimate = estimateText(opened);
  estimated = estimateSeconds(opened);
  say(`Detecting · about ${duration(estimated)} for ${count(opened.frameCount)} frames`, estimate);
  started = performance.now();
  const video = opened, detecting = running = call('Detect', JSON.stringify(run));
  const r = JSON.parse(await detecting);
  const seconds = (performance.now() - started) / 1000;
  if (running !== detecting) return; // abandoned: a new file was chosen
  running = null;
  // The CSVs come as strings of their own, not inside Detect's JSON (README "Large files").
  r.scenesCsv = await call('ScenesCsv');
  r.statsCsv = await call('StatsCsv');
  if (opened !== video) return; // a new file was chosen meanwhile
  show(r, seconds);
  await sound();
}

// Whether a call answered (anything: its value, an error, a cancel) within a second or so: as long as the worker takes
// for a few pauses of Detect's (8 frames apart, Api's PauseEvery) at the rate the run has had, and a second more.
async function answered(call) {
  const frame = framesDone > 0 ? (performance.now() - started) / framesDone : 0;
  let timer;
  const late = new Promise(resolve => { timer = setTimeout(() => resolve(false), 1000 + 24 * frame); });
  const ok = await Promise.race([call.then(() => true, () => true), late]);
  clearTimeout(timer);
  return ok;
}

// The file's sound, after its first run, so detection's time and results stay as they are (decision 2026-10-24-audio.md):
// one pass over the audio packets in the worker. Not the page's error state if it fails: the run's results stand, and
// the panels say why there is no sound.
async function sound() {
  if (measured === opened) return;
  const video = opened, said = statusText.textContent, title = statusText.title;
  measured = video;
  say(`${said} · measuring the sound…`, title);
  let audio;
  try {
    audio = JSON.parse(await call('Audio'));
  } catch (e) {
    if (e.name === 'AbortError') return;
    audio = { notMeasured: e.message, track: null, waveform: null, referenceTone: [], silences: [], peaks: [], loudness: null };
  }
  if (opened !== video) return;
  if (state === 'done' || state === 'stopped') say(said, title);
  emit('audio', { video, audio });
}

// How long the run will take, from the frame count and picture size: this browser's own rate from its last run, or the
// reference rate (REFERENCE_FPS) until it has one.
function estimateText({ width, height, frameCount }) {
  const own = storedRate();
  const rate = own ?? REFERENCE_PIXELS_PER_SECOND;
  const from = own ? 'this browser\'s last run' : 'a reference run of Sintel\'s trailer';
  return `Estimate: about ${duration(estimateSeconds({ width, height, frameCount }))} for ${frameCount} frames at ` +
    `${width}x${height} (${(rate / (width * height)).toFixed(0)} frames per second, from ${from}).`;
}

function estimateSeconds({ width, height, frameCount }) {
  return frameCount * width * height / (storedRate() ?? REFERENCE_PIXELS_PER_SECOND);
}

// The time a run has left: at its own rate once it has gone LEFT_AFTER seconds (before, start-up weighs on the rate
// too much), the estimate's until then.
const LEFT_AFTER = 3;
function left(done, expected, seconds, fps) {
  return seconds >= LEFT_AFTER && fps > 0 ? (expected - done) / fps : Math.max(estimated - seconds, 0);
}

// The pixels per second of this browser's last finished run, if it kept one (storage can be off or throw).
function storedRate() {
  try {
    const rate = Number(localStorage.getItem(RATE_KEY));
    return rate > 0 ? rate : null;
  } catch {
    return null;
  }
}

function storeRate(rate) {
  try { localStorage.setItem(RATE_KEY, String(rate)); } catch { /* the reference rate stays */ }
}

function duration(seconds) {
  if (seconds < 90) return `${Math.max(1, Math.round(seconds))} s`;
  const minutes = Math.round(seconds / 60);
  return minutes < 90 ? `${minutes} min` : `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

// The options as Api's DetectRequest takes them; defaults as none, so the library applies its own.
function options() {
  const kind = detector.value;
  const t = threshold.value.trim();
  const m = minLength.value.trim();
  return {
    detector: kind,
    threshold: t === '' || Number(t) === DEFAULT_THRESHOLDS[kind] ? null : Number(t),
    minSceneLength: m === '' || m === DEFAULT_MIN_LENGTH ? null : m,
  };
}

// Stops the run where it has got to (decision "Cancelling a run"): the worker's 'stop' ends Detect's frames at its next
// pause, and Detect answers with the result for the frames read, which detect shows. Only a worker that doesn't answer
// is terminated: the result is gone with it, and the panels keep the shots they were told and mark where the run had
// got to. Opening can't stop half-way: the worker is terminated, and a run opens the file again.
async function stop() {
  if (state === 'opening') {
    replaceWorker();
    setState('idle');
    say('Cancelled.');
    return;
  }
  if (state !== 'detecting') return;
  setState('stopping');
  say(`Stopping at frame ${count(framesDone)}…`);
  const run = running, video = opened;
  worker.postMessage({ type: 'stop' });
  if (await answered(run) || running !== run) return;
  running = null;
  replaceWorker();
  setState('lost');
  say(`Lost at frame ${count(framesDone)}: the worker didn't answer the stop, so it was restarted, and the run's ` +
    'result went with it.');
  emit('cancelled', { video, done: framesDone, expected: framesExpected });
  follow();
}

function fail(message) {
  setState('error');
  say('');
  error.textContent = message;
}

function show(r, seconds) {
  setState(r.stopped ? 'stopped' : 'done');
  const rate = `${(r.frameCount / seconds).toFixed(0)} frames per second`;
  const read = r.frameCount * r.frameRate.den / r.frameRate.num;
  say(r.stopped
    ? `Stopped at ${timecode(read)}, frame ${count(r.frameCount)} of ${count(opened.frameCount)}, after ` +
      `${seconds.toFixed(1)} s (${rate})`
    : `Done in ${seconds.toFixed(1)} s, ${rate} (${(read / seconds).toFixed(1)} times real time)`,
    `${count(r.frameCount)} frames, ${read.toFixed(3)} s of video, in ${seconds.toFixed(3)} s; ${estimate}`);
  // A run of a few seconds is mostly start-up: too short to measure this browser's speed by.
  if (seconds >= 5) storeRate(r.frameCount * opened.width * opened.height / seconds);
  emit('done', { video: opened, result: r, seconds, name: file.name.replace(/\.[^.]*$/, '') });
  clearShots();
  for (const shot of r.shots) addShot(shot);
  result.hidden = false;
}

// The shot playing, a caption under the video: the last shot starting at or before the playhead, if the playhead is
// before its end; told to the panels as 'playing' (the shots table marks its row). The shots are the ones told while a
// run goes (a lost run's stay), the result's once it is done.
function clearShots() {
  shots = [];
  starts = [];
  ends = [];
  follow();
}

function addShot(shot) {
  shots.push(shot);
  starts.push(secondsOf(shot.startTimecode));
  ends.push(secondsOf(shot.endTimecode));
  follow();
}

function shotAt(t) {
  let lo = 0, hi = starts.length - 1, found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (starts[mid] <= t + 1e-6) { found = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return found >= 0 && t < ends[found] - 1e-6 ? found : -1;
}

function follow() {
  const t = video.currentTime || 0, i = shotAt(t), s = shots[i];
  if (!s) {
    now.textContent = shots.length > 0 ? 'Outside the shots found.' : '';
    now.title = '';
  } else {
    const going = runGoes();
    now.textContent = `Shot ${s.shot}${going ? '' : ` of ${shots.length}`} · ${s.startTimecode} – ${s.endTimecode} · ` +
      `${(t - starts[i]).toFixed(1)} s into ${s.durationSeconds.toFixed(1)} s` +
      `${going ? ` · ${shots.length} ${shots.length === 1 ? 'shot' : 'shots'} so far` : ''}`;
    now.title = `Shot ${s.shot}: ${s.startTimecode} to ${s.endTimecode}; the playhead at ${timecode(t)}`;
  }
  emit('playing', { shot: s ?? null, paused: video.paused });
}

// The video's length on the axis: Open's duration, or its frames over its rate.
const lengthOf = v => v.duration ?? v.frameCount * v.frameRate.den / v.frameRate.num;

// The toolbar's line on the file: its size, and once opened the description's first facts.
function facts(f, v) {
  const rate = Number((v.frameRate.num / v.frameRate.den).toFixed(3));
  return [megabytes(f.size), `${v.width}×${v.height}`, `${rate} fps`, `${lengthOf(v).toFixed(1)} s`,
    v.audio ? v.audio.codec.toUpperCase() : 'no audio'].join(' · ');
}

const count = n => n.toLocaleString('en');

function megabytes(bytes) {
  return `${(bytes / 1024 / 1024).toFixed(bytes < 10 * 1024 * 1024 ? 1 : 0)} MB`;
}

detector.onchange = () => { threshold.value = DEFAULT_THRESHOLDS[detector.value]; };
threshold.value = DEFAULT_THRESHOLDS[detector.value];
fileInput.onchange = () => choose(fileInput.files[0]);
changeButton.onclick = () => fileInput.click();
// The whole drop zone picks a file, not only its label: all but the demo's line.
drop.onclick = e => { if (e.target === drop) fileInput.click(); };
for (const type of ['timeupdate', 'seeking', 'emptied', 'play', 'pause']) video.addEventListener(type, follow);
form.onsubmit = e => { e.preventDefault(); start(); };
stopButton.onclick = stop;
// The demo clip, next to the page (pages.yml), run as if it had been picked.
demoButton.onclick = async () => {
  demoButton.disabled = true;
  say('Downloading the demo clip…');
  try {
    const response = await fetch('demo/sintel-trailer.mp4');
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    await choose(new File([await response.blob()], 'sintel-trailer.mp4', { type: 'video/mp4' }));
    start();
  } catch (e) {
    fail(`The demo clip didn't download: ${e.message}`);
  }
};
// A file dropped anywhere on the page: the drop zone, or the toolbar once one is picked, marked while it is dragged.
for (const type of ['dragenter', 'dragover']) {
  document.addEventListener(type, e => { e.preventDefault(); document.body.classList.add('dragging'); });
}
document.addEventListener('dragleave', e => { if (!e.relatedTarget) document.body.classList.remove('dragging'); });
document.addEventListener('drop', e => {
  e.preventDefault();
  document.body.classList.remove('dragging');
  choose(e.dataTransfer.files[0]);
});
replaceWorker();
setState('idle');
