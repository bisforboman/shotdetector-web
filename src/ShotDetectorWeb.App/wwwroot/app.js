// The page: picks a file, has the worker (worker.js, which owns the .NET runtime) open it and detect, and tells the
// panels what happens. How it is built: docs/decisions/2026-10-24-the-page.md and 2026-10-25-one-page.md. This file
// keeps the page's states (idle, opening, detecting, done, error), the options, the estimate, cancel and the demo; what
// is shown of a file or a run is in the panels (panels/<name>.js, listed in index.html), each a lane of the timeline
// (timeline.js) or a card. A panel imports from here:
//   on(event, f)       f(data) on each of the page's events, in this order for a run:
//                        'chose'    { file }                     a new file: whatever was shown of the last one goes
//                        'opened'   { video }                    the file's YuvVideo, from Api's Open
//                        'run'      { video, options }           a run starts: whatever was shown of the last run goes
//                        'progress' { video, done, expected }    frames done and expected (0 when unknown)
//                        'shot'     { video, shot }              a shot as soon as ShotDetector knows it
//                        'done'     { video, result, seconds, name }   the run's result (Api's Detect, its CSVs as
//                                                                scenesCsv and statsCsv); name: the file's, without its
//                                                                extension, for downloads
//                        'cancelled' { video, done, expected }   the run was cancelled at frame done
//   call(name, ...args) an export of Api's, by name, in the worker: a promise of what it returns
//   card(title, { below }) a card beside the video (below: under it, full width), for the panel to fill
import { setDuration } from './timeline.js';

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
const detectButton = $('detect'), cancelButton = $('cancel'), demoButton = $('demo');
const progress = $('progress'), statusText = $('status-text'), error = $('error');
const result = $('result'), video = $('video'), cards = $('cards'), below = $('below');

let file = null;        // the chosen file
let opened = null;      // the file's YuvVideo, once the current worker has it open
let estimate = '';      // the estimate of the current run, as the status shows it
let state = 'idle';
let started = 0;        // when the current detection started (performance.now())
let framesDone = 0;     // frames the current run has done, as its last progress report said
let framesExpected = 0; // and how many it expects (0 when unknown)

const listeners = {};
export function on(event, f) {
  (listeners[event] ??= []).push(f);
}

function emit(event, data) {
  for (const f of listeners[event] ?? []) f(data);
}

export function card(title, { below: under = false } = {}) {
  const section = document.createElement('section');
  section.className = 'card';
  const heading = document.createElement('h2');
  heading.textContent = title;
  section.append(heading);
  (under ? below : cards).append(section);
  return section;
}

// The worker and its calls: each call has an id, and the worker answers them in order. A worker that is replaced (a
// cancel, a crash) takes its calls with it: they reject with an AbortError, which the page doesn't show.
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
  // Only the current worker's: one terminated by a cancel may still have messages on their way.
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
  progress({ done, expected }) {
    if (state !== 'detecting') return;
    framesDone = done;
    framesExpected = expected;
    if (expected > 0) progress.value = Math.min(done / expected, 1);
    const seconds = (performance.now() - started) / 1000;
    const fps = seconds > 0 ? done / seconds : 0;
    statusText.textContent = `Detecting: frame ${done}${expected > 0 ? ` of ${expected}` : ''}, ` +
      `${fps.toFixed(0)} frames per second. ${estimate}`;
    emit('progress', { video: opened, done, expected });
  },
  // A shot as soon as ShotDetector knows it: a few frames after the cut that ends it with one detector (what the page
  // sends), the same object as in the result's shots (tools/app-check.mjs checks they are the same).
  shot({ shot }) {
    if (state === 'detecting') emit('shot', { video: opened, shot });
  },
};

function setState(next) {
  state = next;
  const busy = next === 'opening' || next === 'detecting';
  document.body.dataset.state = next;
  fileInput.disabled = busy;
  for (const input of [detector, threshold, minLength]) input.disabled = busy;
  detectButton.disabled = busy || !file;
  demoButton.disabled = busy;
  detectButton.hidden = busy;
  cancelButton.hidden = !busy;
  progress.hidden = !busy;
  error.hidden = next !== 'error';
  if (next === 'opening') result.hidden = true;
}

function choose(chosen) {
  if (!chosen || state === 'opening' || state === 'detecting') return;
  opened = null;
  result.hidden = true;
  setDuration(0);
  emit('chose', { file: chosen });
  dropText.textContent = `${chosen.name} (${megabytes(chosen.size)})`;
  file = chosen;
  if (video.src) URL.revokeObjectURL(video.src);
  video.src = URL.createObjectURL(file);
  statusText.textContent = '';
  setState('idle');
}

// Opens the file if the current worker hasn't, then detects. Errors are the library's messages; a call that a cancel
// took away is no error.
async function start() {
  if (!file) return;
  error.textContent = '';
  try {
    if (!opened) {
      setState('opening');
      progress.removeAttribute('value');
      statusText.textContent = `Opening ${file.name}…`;
      // The File itself: the worker reads it in slices as .NET needs them, so it is never copied whole (worker.js).
      opened = JSON.parse(await call('Open', file));
      setDuration(opened.duration ?? opened.frameCount * opened.frameRate.den / opened.frameRate.num);
      emit('opened', { video: opened });
    }
    await detect();
  } catch (e) {
    if (e.name !== 'AbortError') fail(e.message);
  }
}

async function detect() {
  setState('detecting');
  framesDone = framesExpected = 0;
  const run = options();
  emit('run', { video: opened, options: run });
  result.hidden = false;
  progress.removeAttribute('value');
  estimate = estimateText(opened);
  statusText.textContent = `Detecting… ${estimate}`;
  started = performance.now();
  const r = JSON.parse(await call('Detect', JSON.stringify(run)));
  // The CSVs come as strings of their own, not inside Detect's JSON (README "Large files").
  r.scenesCsv = await call('ScenesCsv');
  r.statsCsv = await call('StatsCsv');
  show(r, (performance.now() - started) / 1000);
}

// How long the run will take, from the frame count and picture size: this browser's own rate from its last run, or the
// reference rate (REFERENCE_FPS) until it has one.
function estimateText({ width, height, frameCount }) {
  const own = storedRate();
  const rate = own ?? REFERENCE_PIXELS_PER_SECOND;
  const seconds = frameCount * width * height / rate;
  const from = own ? 'this browser\'s last run' : 'a reference run of Sintel\'s trailer';
  return `Estimate: about ${duration(seconds)} for ${frameCount} frames at ${width}x${height} ` +
    `(${(rate / (width * height)).toFixed(0)} frames per second, from ${from}).`;
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

// Terminates the worker (decision "Cancelling a run"): the result is gone with it; the panels keep what they were told
// and mark where the run stopped.
function cancel() {
  const wasDetecting = state === 'detecting', video = opened;
  replaceWorker();
  setState('idle');
  statusText.textContent = 'Cancelled.';
  if (wasDetecting) emit('cancelled', { video, done: framesDone, expected: framesExpected });
}

function fail(message) {
  setState('error');
  statusText.textContent = '';
  error.textContent = message;
}

function show(r, seconds) {
  setState('done');
  statusText.textContent = `Done in ${seconds.toFixed(1)} s (${(r.frameCount / seconds).toFixed(0)} frames per second).`;
  // A run of a few seconds is mostly start-up: too short to measure this browser's speed by.
  if (seconds >= 5) storeRate(r.frameCount * opened.width * opened.height / seconds);
  emit('done', { video: opened, result: r, seconds, name: file.name.replace(/\.[^.]*$/, '') });
  result.hidden = false;
}

function megabytes(bytes) {
  return `${(bytes / 1024 / 1024).toFixed(bytes < 10 * 1024 * 1024 ? 1 : 0)} MB`;
}

detector.onchange = () => { threshold.value = DEFAULT_THRESHOLDS[detector.value]; };
threshold.value = DEFAULT_THRESHOLDS[detector.value];
fileInput.onchange = () => choose(fileInput.files[0]);
form.onsubmit = e => { e.preventDefault(); start(); };
cancelButton.onclick = cancel;
// The demo clip, next to the page (pages.yml), run as if it had been picked.
demoButton.onclick = async () => {
  demoButton.disabled = true;
  statusText.textContent = 'Downloading the demo clip…';
  try {
    const response = await fetch('demo/sintel-trailer.mp4');
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    choose(new File([await response.blob()], 'sintel-trailer.mp4', { type: 'video/mp4' }));
    start();
  } catch (e) {
    fail(`The demo clip didn't download: ${e.message}`);
  }
};
for (const type of ['dragenter', 'dragover']) {
  document.addEventListener(type, e => { e.preventDefault(); drop.classList.add('over'); });
}
document.addEventListener('dragleave', e => { if (!e.relatedTarget) drop.classList.remove('over'); });
document.addEventListener('drop', e => {
  e.preventDefault();
  drop.classList.remove('over');
  choose(e.dataTransfer.files[0]);
});
replaceWorker();
setState('idle');
