// The page: picks a file, sends it to the worker (worker.js, which owns the .NET runtime) and shows what comes back.
// How it is built: docs/decisions/2026-10-24-the-page.md. States: idle, opening, detecting, done, error.

// Files above this are refused before they are read: the whole file goes into the wasm heap (decision "The file in
// memory").
const MAX_FILE_BYTES = 512 * 1024 * 1024;

// ShotDetector's default threshold for each detector (DetectionOptions.Threshold), prefilled when one is picked. A
// threshold left at its default is sent as none, so the run is the library's own default run.
const DEFAULT_THRESHOLDS = { adaptive: 3, content: 27, threshold: 12, histogram: 0.05, hash: 0.395 };
const DEFAULT_MIN_LENGTH = '0.6s';

const $ = id => document.getElementById(id);
const form = $('form'), fileInput = $('file'), drop = $('drop'), dropText = $('drop-text');
const detector = $('detector'), threshold = $('threshold'), minLength = $('min-length');
const detectButton = $('detect'), cancelButton = $('cancel');
const progress = $('progress'), statusText = $('status-text'), error = $('error');
const result = $('result'), video = $('video'), summary = $('summary'), shotsBody = $('shots').tBodies[0];
const scenesLink = $('scenes-csv'), statsLink = $('stats-csv');

let file = null;        // the chosen file
let opened = false;     // whether the current worker has it open
let state = 'idle';
let started = 0;        // when the current detection started (performance.now())
let worker = newWorker();

function newWorker() {
  const w = new Worker('worker.js', { type: 'module' });
  w.onmessage = ({ data }) => handlers[data.type]?.(data);
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
  opened = false;
}

const handlers = {
  opened() {
    opened = true;
    detect();
  },
  progress({ done, expected }) {
    if (state !== 'detecting') return;
    if (expected > 0) progress.value = Math.min(done / expected, 1);
    const seconds = (performance.now() - started) / 1000;
    const fps = seconds > 0 ? done / seconds : 0;
    statusText.textContent = `Detecting: frame ${done}${expected > 0 ? ` of ${expected}` : ''}, ${fps.toFixed(0)} frames per second`;
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
  detectButton.hidden = busy;
  cancelButton.hidden = !busy;
  progress.hidden = !busy;
  error.hidden = next !== 'error';
  if (busy) result.hidden = true;
}

function choose(chosen) {
  if (!chosen || state === 'opening' || state === 'detecting') return;
  file = null;
  opened = false;
  result.hidden = true;
  clearResult();
  dropText.textContent = `${chosen.name} (${megabytes(chosen.size)})`;
  if (chosen.size > MAX_FILE_BYTES) {
    fail(`${chosen.name} is ${megabytes(chosen.size)}: this page reads files up to ${megabytes(MAX_FILE_BYTES)}, ` +
      'which is trailers, not films, yet. Larger files are coming.');
    return;
  }
  file = chosen;
  if (video.src) URL.revokeObjectURL(video.src);
  video.src = URL.createObjectURL(file);
  statusText.textContent = '';
  setState('idle');
}

async function start() {
  if (!file) return;
  error.textContent = '';
  if (opened) {
    detect();
    return;
  }
  setState('opening');
  progress.removeAttribute('value');
  statusText.textContent = `Opening ${file.name}…`;
  const chosen = file;
  const bytes = await chosen.arrayBuffer();
  if (state !== 'opening' || file !== chosen) return; // cancelled while reading
  worker.postMessage({ type: 'open', file: bytes }, [bytes]);
}

function detect() {
  setState('detecting');
  progress.removeAttribute('value');
  statusText.textContent = 'Detecting…';
  started = performance.now();
  worker.postMessage({ type: 'detect', options: options() });
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

function cancel() {
  replaceWorker();
  setState('idle');
  statusText.textContent = 'Cancelled.';
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
  summary.textContent = `${r.shots.length} ${r.shots.length === 1 ? 'shot' : 'shots'} in ${r.frameCount} frames ` +
    `at ${Number(rate.toFixed(3))} fps${r.duration != null ? `, ${r.duration.toFixed(2)} s` : ''}.`;

  const base = file.name.replace(/\.[^.]*$/, '');
  link(scenesLink, r.scenesCsv, `${base}.scenes.csv`);
  link(statsLink, r.statsCsv, `${base}.stats.csv`);

  for (const shot of r.shots) {
    const row = shotsBody.insertRow();
    for (const text of [shot.shot, shot.startTimecode, shot.endTimecode, `${shot.durationSeconds.toFixed(3)} s`])
      row.insertCell().textContent = text;
    row.tabIndex = 0;
    row.title = `Play from ${shot.startTimecode}`;
    row.onclick = () => seek(shot.startTimecode);
    row.onkeydown = e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); seek(shot.startTimecode); } };
  }
  result.hidden = false;
}

function seek(timecode) {
  const [h, m, s] = timecode.split(':').map(Number);
  video.currentTime = h * 3600 + m * 60 + s;
  video.play().catch(() => {}); // a browser that can't play the file still shows where the shot is
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
}

function megabytes(bytes) {
  return `${(bytes / 1024 / 1024).toFixed(bytes < 10 * 1024 * 1024 ? 1 : 0)} MB`;
}

detector.onchange = () => { threshold.value = DEFAULT_THRESHOLDS[detector.value]; };
threshold.value = DEFAULT_THRESHOLDS[detector.value];
fileInput.onchange = () => choose(fileInput.files[0]);
form.onsubmit = e => { e.preventDefault(); start(); };
cancelButton.onclick = cancel;
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
