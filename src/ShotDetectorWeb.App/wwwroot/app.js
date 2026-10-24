// The page: picks a file, sends it to the worker (worker.js, which runs .NET), shows progress and the shots, offers
// the CSVs. States: idle, opening, detecting, done, error (docs/decisions/2026-10-24-the-page.md, "The page itself").

// Files above this are refused before they are read: the whole file is copied into the worker's memory
// (decision "The file in memory"; the backlog row "Large files" streams it instead).
const MAX_BYTES = 512 * 1024 * 1024;

// ShotDetector's default threshold for each detector (scenedetect's), prefilled when one is picked.
const DEFAULT_THRESHOLD = { adaptive: 3, content: 27, threshold: 12, histogram: 0.05, hash: 0.395 };

const $ = id => document.getElementById(id);
const form = $('run'), fileInput = $('file'), detector = $('detector'), threshold = $('threshold');
const minLength = $('min-length'), detectButton = $('detect'), cancelButton = $('cancel');
const message = $('message'), progress = $('progress'), video = $('video'), shotRows = $('shots');

let chosen = null;     // the File picked or dropped
let worker = null;     // the worker running .NET
let opened = null;     // the File the worker has open, if any
let pending = null;    // { resolve, reject } for the worker's answer to the message in flight
let started = 0;       // when detecting began (performance.now())
let csvUrls = [];      // object URLs of the downloads, revoked on the next run

const cancelled = new Error('Cancelled');

// The worker answers messages in order, one at a time; progress comes while a detect runs. A run can't be interrupted
// from here (the worker is busy inside .NET), so Cancel terminates the worker and starts another.
function startWorker() {
  worker?.terminate();
  opened = null;
  worker = new Worker('worker.js', { type: 'module' });
  worker.onmessage = ({ data }) => {
    if (data.type === 'progress') return showProgress(data.done, data.expected);
    const answer = pending;
    pending = null;
    if (data.type === 'error') answer?.reject(new Error(data.message));
    else answer?.resolve(data);
  };
  // The worker itself failed (it didn't load, or the runtime crashed): this one is of no more use.
  worker.onerror = event => {
    event.preventDefault();
    const answer = pending;
    pending = null;
    startWorker();
    const error = new Error(`The page's worker stopped: ${event.message || 'it did not start'}`);
    if (answer) answer.reject(error);
    else show('error', error.message);
  };
}

function ask(request, transfer = []) {
  return new Promise((resolve, reject) => {
    pending = { resolve, reject };
    worker.postMessage(request, transfer);
  });
}

function show(state, text = '') {
  document.body.dataset.state = state;
  message.textContent = text;
  const busy = state === 'opening' || state === 'detecting';
  progress.hidden = !busy;
  if (state === 'opening') progress.removeAttribute('value');
  cancelButton.hidden = !busy;
  for (const control of [fileInput, detector, threshold, minLength]) control.disabled = busy;
  detectButton.disabled = busy || !chosen || chosen.size > MAX_BYTES;
  if (state !== 'done') $('result').hidden = true;
}

function showProgress(done, expected) {
  const seconds = (performance.now() - started) / 1000;
  const fps = seconds > 0 ? Math.round(done / seconds) : 0;
  if (expected > 0) {
    progress.max = expected;
    progress.value = done;
    message.textContent = `Detecting: frame ${done} of ${expected}, ${fps} frames a second.`;
  } else {
    progress.removeAttribute('value');
    message.textContent = `Detecting: frame ${done}, ${fps} frames a second.`;
  }
}

function choose(file) {
  if (!file) return;
  chosen = file;
  $('file-name').textContent = file.name;
  URL.revokeObjectURL(video.src);
  video.src = URL.createObjectURL(file);
  $('player').hidden = false;
  if (file.size > MAX_BYTES) {
    show('error', `${file.name} is ${megabytes(file.size)}: this page reads files up to ${megabytes(MAX_BYTES)} ` +
      `for now (trailers, not films, yet).`);
  } else {
    show('idle', `${file.name}, ${megabytes(file.size)}.`);
  }
}

async function run() {
  const file = chosen;
  const options = {
    detector: detector.value,
    threshold: threshold.value === '' ? null : Number(threshold.value),
    minSceneLength: minLength.value.trim() || null,
  };
  try {
    if (opened !== file) {
      opened = null;
      show('opening', `Opening ${file.name}: reading it and decoding the first frame.`);
      const bytes = await file.arrayBuffer();
      await ask({ type: 'open', file: bytes }, [bytes]);
      opened = file;
    }
    show('detecting', 'Detecting…');
    started = performance.now();
    const { result } = await ask({ type: 'detect', options });
    showResult(file, result, (performance.now() - started) / 1000);
  } catch (e) {
    if (e !== cancelled) show('error', e.message);
  }
}

function showResult(file, result, seconds) {
  const shots = result.shots;
  show('done', `${shots.length} ${shots.length === 1 ? 'shot' : 'shots'} in ${result.frameCount} frames ` +
    `(${result.frameRate.value.toFixed(3).replace(/\.?0+$/, '')} fps), detected in ${seconds.toFixed(1)} s.`);

  for (const url of csvUrls) URL.revokeObjectURL(url);
  const stem = file.name.replace(/\.[^.]*$/, '');
  csvUrls = [download($('scenes-csv'), result.scenesCsv, `${stem}.scenes.csv`),
    download($('stats-csv'), result.statsCsv, `${stem}.stats.csv`)];

  // Half a frame in, so that a seek lands on the shot's first frame and not the end of the one before.
  const half = 0.5 / result.frameRate.value;
  shotRows.replaceChildren(...shots.map(shot => {
    const row = document.createElement('tr');
    row.dataset.start = toSeconds(shot.startTimecode);
    row.dataset.end = toSeconds(shot.endTimecode);
    const seek = document.createElement('button');
    seek.type = 'button';
    seek.textContent = shot.shot;
    seek.title = `Play from shot ${shot.shot}`;
    seek.onclick = () => { video.currentTime = Number(row.dataset.start) + half; video.play().catch(() => {}); };
    row.onclick = event => { if (event.target !== seek) seek.click(); };
    row.append(cell(seek), cell(shot.startTimecode), cell(shot.endTimecode),
      cell(`${shot.durationSeconds.toFixed(3)} s`, `${shot.durationFrames} frames`));
    return row;
  }));
  $('result').hidden = false;
}

// Shots.Json's timecodes, HH:MM:SS.mmm, in seconds.
function toSeconds(timecode) {
  const [h, m, s] = timecode.split(':').map(Number);
  return h * 3600 + m * 60 + s;
}

function cell(content, title) {
  const td = document.createElement('td');
  td.append(content);
  if (title) td.title = title;
  return td;
}

function download(link, text, name) {
  link.href = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
  link.download = name;
  return link.href;
}

function megabytes(bytes) {
  return `${(bytes / 1024 / 1024).toFixed(bytes < 10 * 1024 * 1024 ? 1 : 0)} MB`;
}

// A file the browser can't play (the page may still read it, or refuse it): no player.
video.onerror = () => { $('player').hidden = true; };

// The row of the shot playing.
video.ontimeupdate = () => {
  for (const row of shotRows.rows) {
    row.classList.toggle('playing', video.currentTime >= row.dataset.start && video.currentTime < row.dataset.end);
  }
};

detector.onchange = () => { threshold.value = DEFAULT_THRESHOLD[detector.value]; };
fileInput.onchange = () => choose(fileInput.files[0]);
form.onsubmit = event => { event.preventDefault(); if (chosen && chosen.size <= MAX_BYTES) run(); };
cancelButton.onclick = () => {
  const answer = pending;
  pending = null;
  startWorker();
  answer?.reject(cancelled);
  show('idle', 'Cancelled.');
};

// A drop anywhere on the page picks the file (and keeps the browser from opening it instead).
const drop = $('drop');
document.addEventListener('dragover', event => {
  event.preventDefault();
  drop.classList.add('over');
});
document.addEventListener('dragleave', event => { if (!event.relatedTarget) drop.classList.remove('over'); });
document.addEventListener('drop', event => {
  event.preventDefault();
  drop.classList.remove('over');
  if (fileInput.disabled) return;
  choose(event.dataTransfer.files[0]);
});

threshold.value = DEFAULT_THRESHOLD[detector.value];
startWorker();
show('idle');
