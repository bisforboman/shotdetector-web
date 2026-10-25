// The page: picks a file, sends it to the worker (worker.js, which owns the .NET runtime) and shows what comes back.
// How it is built: docs/decisions/2026-10-24-the-page.md. States: idle, opening, detecting, done, error. The shots
// come in while a run goes and stay when it is cancelled, with a line marked unfinished up to where it stopped.

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
const result = $('result'), video = $('video'), summary = $('summary'), shotsBody = $('shots').tBodies[0];
const downloads = document.querySelector('.downloads'), scenesLink = $('scenes-csv'), statsLink = $('stats-csv');

let file = null;        // the chosen file
let opened = null;      // the file's YuvVideo, once the current worker has it open
let estimate = '';      // the estimate of the current run, as the status shows it
let state = 'idle';
let started = 0;        // when the current detection started (performance.now())
let found = [];         // the shots the current run has told so far, in order
let framesDone = 0;     // frames the current run has done, as its last progress report said
let framesExpected = 0; // and how many it expects (0 when unknown)
let worker = newWorker();

function newWorker() {
  const w = new Worker('worker.js', { type: 'module' });
  // Only the current worker's: one terminated by a cancel may still have messages on their way.
  w.onmessage = ({ data }) => { if (w === worker) handlers[data.type]?.(data); };
  // Not an exception from .NET (those come as 'error' messages): the worker itself failed, e.g. out of memory.
  w.onerror = e => {
    e.preventDefault();
    replaceWorker();
    fail(`The worker stopped: ${e.message || 'unknown error'}`);
  };
  return w;
}

function replaceWorker() {
  worker.terminate();
  worker = newWorker();
  opened = null;
}

const handlers = {
  opened({ video }) {
    opened = video;
    detect();
  },
  progress({ done, expected }) {
    if (state !== 'detecting') return;
    framesDone = done;
    framesExpected = expected;
    if (expected > 0) progress.value = Math.min(done / expected, 1);
    const seconds = (performance.now() - started) / 1000;
    const fps = seconds > 0 ? done / seconds : 0;
    statusText.textContent = `Detecting: frame ${done}${expected > 0 ? ` of ${expected}` : ''}, ` +
      `${fps.toFixed(0)} frames per second. ${estimate}`;
  },
  // A shot as soon as ShotDetector knows it: a few frames after the cut that ends it with one detector (what the page
  // sends), the same object as in the result's shots (tools/app-check.mjs checks they are the same).
  shot({ shot }) {
    if (state !== 'detecting') return;
    found.push(shot);
    addRow(shot);
    summary.textContent = `${found.length} ${found.length === 1 ? 'shot' : 'shots'} so far.`;
  },
  detected({ result }) {
    show(result, (performance.now() - started) / 1000);
  },
  error({ message }) {
    fail(message);
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
  clearResult();
  dropText.textContent = `${chosen.name} (${megabytes(chosen.size)})`;
  file = chosen;
  if (video.src) URL.revokeObjectURL(video.src);
  video.src = URL.createObjectURL(file);
  statusText.textContent = '';
  setState('idle');
}

function start() {
  if (!file) return;
  error.textContent = '';
  if (opened) {
    detect();
    return;
  }
  setState('opening');
  progress.removeAttribute('value');
  statusText.textContent = `Opening ${file.name}…`;
  // The File itself: the worker reads it in slices as .NET needs them, so it is never copied whole (worker.js).
  worker.postMessage({ type: 'open', file });
}

function detect() {
  setState('detecting');
  clearResult();
  found = [];
  framesDone = framesExpected = 0;
  summary.textContent = 'The shots appear here as they are found.';
  result.hidden = false;
  progress.removeAttribute('value');
  estimate = estimateText(opened);
  statusText.textContent = `Detecting… ${estimate}`;
  started = performance.now();
  worker.postMessage({ type: 'detect', options: options() });
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

// Terminates the worker (decision "Cancelling a run"): the result is gone with it, the shots told so far stay, and one
// line, not a shot, goes from the last cut to the frame the run had reached.
function cancel() {
  const wasDetecting = state === 'detecting', rate = opened?.frameRate;
  replaceWorker();
  setState('idle');
  statusText.textContent = 'Cancelled.';
  if (!wasDetecting) return;
  const { num, den } = rate;
  const from = found.length > 0 ? secondsOf(found.at(-1).endTimecode) : 0;
  const to = framesDone * den / num;
  if (to > from) addUnfinished(from, to);
  summary.textContent = `Cancelled at frame ${framesDone}${framesExpected > 0 ? ` of ${framesExpected}` : ''}: ` +
    `${found.length} ${found.length === 1 ? 'shot' : 'shots'} found` +
    `${to > from ? ', then one unfinished up to where the run stopped' : ''}. No downloads for a cancelled run.`;
}

function fail(message) {
  setState('error');
  statusText.textContent = '';
  error.textContent = message;
}

function show(r, seconds) {
  setState('done');
  clearResult();
  const rate = r.frameRate.num / r.frameRate.den;
  statusText.textContent = `Done in ${seconds.toFixed(1)} s (${(r.frameCount / seconds).toFixed(0)} frames per second).`;
  // A run of a few seconds is mostly start-up: too short to measure this browser's speed by.
  if (seconds >= 5) storeRate(r.frameCount * opened.width * opened.height / seconds);
  summary.textContent = `${r.shots.length} ${r.shots.length === 1 ? 'shot' : 'shots'} in ${r.frameCount} frames ` +
    `at ${Number(rate.toFixed(3))} fps${r.duration != null ? `, ${r.duration.toFixed(2)} s` : ''}.`;

  const base = file.name.replace(/\.[^.]*$/, '');
  link(scenesLink, r.scenesCsv, `${base}.scenes.csv`);
  link(statsLink, r.statsCsv, `${base}.stats.csv`);

  // The result's shots, which are the ones told while it ran: the table is the result's.
  for (const shot of r.shots) addRow(shot);
  downloads.hidden = false;
  result.hidden = false;
}

function addRow(shot) {
  row(shotsBody.insertRow(), [shot.shot, shot.startTimecode, shot.endTimecode, `${shot.durationSeconds.toFixed(3)} s`],
    secondsOf(shot.startTimecode));
}

// The part of the video after the last cut that a cancelled run had read: the page's own times (frames done over the
// frame rate), not a shot of ShotDetector's, so it says so.
function addUnfinished(from, to) {
  const r = shotsBody.insertRow();
  r.className = 'unfinished';
  row(r, ['unfinished', timecode(from), timecode(to), `${(to - from).toFixed(3)} s`], from);
  r.title = `Not a shot: the run was cancelled here. Play from ${timecode(from)}`;
}

function row(r, texts, at) {
  for (const text of texts) r.insertCell().textContent = text;
  r.tabIndex = 0;
  r.title = `Play from ${texts[1]}`;
  r.onclick = () => seek(at);
  r.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); seek(at); } };
}

function seek(at) {
  video.currentTime = at;
  video.play().catch(() => {}); // a browser that can't play the file still shows where the shot is
}

// A timecode as ShotDetector prints them (HH:MM:SS.mmm) in seconds, and back.
function secondsOf(tc) {
  const [h, m, s] = tc.split(':').map(Number);
  return h * 3600 + m * 60 + s;
}

function timecode(s) {
  const ms = Math.round(s * 1000);
  const pad = (n, w) => String(n).padStart(w, '0');
  return `${pad(Math.floor(ms / 3600000), 2)}:${pad(Math.floor(ms / 60000) % 60, 2)}:${pad(Math.floor(ms / 1000) % 60, 2)}` +
    `.${pad(ms % 1000, 3)}`;
}

function link(a, text, name) {
  a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
  a.download = name;
}

function clearResult() {
  for (const a of [scenesLink, statsLink]) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
  }
  shotsBody.replaceChildren();
  summary.textContent = '';
  downloads.hidden = true;
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
setState('idle');
