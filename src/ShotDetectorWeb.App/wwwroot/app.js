// The page: picks a file, has the worker (worker.js, which owns the .NET runtime) open it and detect, and tells the
// panels what happens. How it is built: docs/decisions/2026-10-24-the-page.md and 2026-10-25-one-page.md. This file
// keeps the page's states (idle, opening, detecting, stopping, done, stopped, lost, error), the landing that folds into
// a toolbar once a file is picked, the one-line status, the options, the estimate, stop, the demo and the shot playing
// under the video (decision 2026-10-26-new-ui.md), a long video split across workers and the sound's own worker
// (decision 2026-10-26-workers.md); what else is shown of a file or a run is in the panels
// (panels/<name>.js, listed in index.html), each a lane of the timeline (timeline.js) or a card. A panel imports from
// here:
//   on(event, f)       f(data) on each of the page's events, in this order for a run:
//                        'chose'    { file }                     a new file: whatever was shown of the last one goes
//                        'opened'   { video }                    the file's YuvVideo, from Api's Open
//                        'run'      { video, options, ranges, fallback }   a run starts: whatever was shown of the
//                                                                last run goes. ranges: a run split across workers
//                                                                (decision 2026-10-26-workers.md), Api's Ranges'
//                                                                [{ firstFrame, endFrame }], else null; fallback: why a
//                                                                split run is being detected again with one worker
//                        'progress' { video, done, expected }    frames done and expected (0 when unknown), one worker
//                        'range'    { video, index, done, of }   a split run's range: its frames done, of how many
//                        'shot'     { video, shot }              a shot as soon as ShotDetector knows it
//                        'done'     { video, result, seconds, name }   the run's result (Api's Detect, its CSVs as
//                                                                scenesCsv and statsCsv); name: the file's, without its
//                                                                extension, for downloads. A stopped run's too, with
//                                                                result.stopped: the result for its frameCount frames
//                        'cancelled' { video, done, expected }   the run was lost at frame done: the worker didn't
//                                                                answer a stop and was terminated
//                        'audio'    { video, audio }             the file's sound, measured in a worker of its own
//                                                                alongside the file's first run: Api's Audio
//                                                                (CLAUDE.md "Layout" has its JSON), once per opened
//                                                                file; a failure as notMeasured
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

// A long video is split across several workers, each scoring a range of it from a keyframe, and their scores merged
// (decision 2026-10-26-workers.md): above SPLIT_SECONDS of video, as many workers as the machine has cores but one, at
// most MOST_WORKERS. The page's address can say how many (?workers=N; 1: never split), whatever the video's length.
// Measured 2026-10-27 (README "Long videos across workers"): splitting paid from Sintel's 52 s trailer up, so the
// decision's 10 minutes became 2, below which a run is short enough to keep its live shots (the demo's among them); 4
// workers took a two-hour film's renderer to 1.6 GB, so 4 stays the most.
const SPLIT_SECONDS = 120;
const MOST_WORKERS = 4;
const ASKED_WORKERS = Number(new URLSearchParams(location.search).get('workers')) || null;
// What Api's Merge says first when the ranges don't make up the video: the run is detected again with one worker.
const NOT_MERGED = 'The ranges didn\'t merge: ';
// How many times as fast a run goes split across 2, 3 and 4 workers as on one, for the estimate: measured on a
// two-hour film on a 4-core machine (README "Long videos across workers").
const SPLIT_SPEEDUP = { 2: 1.97, 3: 2.93, 4: 3.6 };

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
let split = null;       // a split run while it goes: { frames, done (each range's), stopped, stop() resolving it }
let helpers = [];       // the workers of a split run's ranges after the first, ended with the run
let listening = null;   // the worker measuring the opened file's sound, until it has
let shown = null;       // what the status says of the last result, { text, title }
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

// The workers and their calls: the page's own worker (worker), which opens the file and runs, and the workers of a
// split run's other ranges and of the sound, each a worker.js with a runtime of its own. Each call has an id, and a
// worker answers its calls in order. A worker that is ended (replaced: one that didn't answer a stop, a crash; or done
// with) takes its calls with it, and a call the worker cancelled ends the same way: they reject with an AbortError,
// which the page doesn't show.
let worker, lastId = 0;
const calls = new Map(), ended = new WeakSet();

export function call(name, ...args) {
  return callOn(worker, name, ...args);
}

function callOn(w, name, ...args) {
  return new Promise((resolve, reject) => {
    const id = ++lastId;
    calls.set(id, { resolve, reject, worker: w });
    w.postMessage({ type: 'call', id, name, args });
  });
}

// A new worker; its progress and shots go to the handlers given (the page's own worker's: fromWorker's), and a failure
// of its own to failed (the page's own worker's: the page's error).
function newWorker({ progress = fromWorker.progress, shot = fromWorker.shot, failed } = {}) {
  const w = new Worker('worker.js', { type: 'module' });
  const handlers = { ...fromWorker, progress, shot };
  // Only a live worker's: one ended may still have messages on their way.
  w.onmessage = ({ data }) => { if (!ended.has(w)) handlers[data.type]?.(data); };
  // Not an exception from .NET (those answer a call as 'error'): the worker itself failed, e.g. out of memory.
  w.onerror = e => {
    e.preventDefault();
    const message = `The worker stopped: ${e.message || 'unknown error'}`;
    if (failed) {
      end(w, new Error(message));
      failed(message);
    } else {
      replaceWorker();
      fail(message);
    }
  };
  return w;
}

// Terminates a worker; its calls reject with error (an AbortError unless said).
function end(w, error = new DOMException('The worker was replaced.', 'AbortError')) {
  if (!w || ended.has(w)) return;
  w.terminate();
  ended.add(w);
  for (const [id, c] of calls) {
    if (c.worker !== w) continue;
    calls.delete(id);
    c.reject(error);
  }
}

function replaceWorker() {
  end(worker);
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
    if (split) return rangeProgress(0, done, expected);
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
// keeps nothing and the worker stays, or the worker replaced if it doesn't answer; a split run's other workers and the
// sound's are ended.
async function choose(chosen) {
  if (!chosen || state === 'opening') return;
  if (running) {
    const run = running;
    running = null;
    worker.postMessage({ type: 'cancel' });
    endHelpers();
    if (!await answered(run)) replaceWorker();
  }
  end(listening?.worker);
  listening = null;
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

// One run on the opened file, split across workers for a long video (ranges), the sound measured alongside it the
// first time. Stop (stop) ends it early with the result for the frames read, which is shown as a finished run's is; a
// new file (choose) abandons it.
async function detect() {
  setState('detecting');
  const run = options(), plan = await ranges(opened);
  listen();
  await go(run, plan);
}

// The run itself: on one worker (Api's Detect) or split (plan: splitRun). A split run whose ranges don't merge, or one of
// whose workers fails, is detected again with one worker, saying why (fallback).
async function go(run, plan, fallback = null) {
  framesDone = framesExpected = 0;
  emit('run', { video: opened, options: run, ranges: plan?.ranges ?? null, fallback });
  clearShots();
  result.hidden = false;
  progress.removeAttribute('value');
  const workers = plan?.ranges.length ?? 1;
  estimate = estimateText(opened, workers);
  estimated = estimateSeconds(opened, workers);
  say(`${fallback ? `${fallback} Detecting again with one worker` : 'Detecting'} · about ${duration(estimated)} for ` +
    `${count(opened.frameCount)} frames${workers > 1 ? ` with ${workers} workers` : ''}`, estimate);
  started = performance.now();
  const video = opened, detecting = running = plan ? splitRun(plan, run) : call('Detect', JSON.stringify(run));
  let json;
  try {
    json = await detecting;
  } catch (e) {
    if (running !== detecting) return; // abandoned: a new file was chosen
    if (e.name === 'TimeoutError') return lost(video);
    if (!plan || state === 'stopping' || e.name === 'AbortError') throw e;
    running = null;
    return go(run, null, e.message.startsWith(NOT_MERGED) ? e.message : `A worker failed: ${e.message}`);
  }
  const r = JSON.parse(json);
  const seconds = (performance.now() - started) / 1000;
  if (running !== detecting) return; // abandoned: a new file was chosen
  running = null;
  // The CSVs come as strings of their own, not inside Detect's JSON (README "Large files").
  r.scenesCsv = await call('ScenesCsv');
  r.statsCsv = await call('StatsCsv');
  if (opened !== video) return; // a new file was chosen meanwhile
  show(r, seconds, workers);
}

// How a run of the opened file is split (decision 2026-10-26-workers.md): above SPLIT_SECONDS of video, into as many
// ranges as there are cores but one, at most MOST_WORKERS (or as ?workers= says), each from a keyframe (Api's Ranges):
// { count, frames, ranges }, or null for one worker (a short video, a machine with two cores, too few keyframes).
async function ranges(video) {
  const count = ASKED_WORKERS ??
    (lengthOf(video) > SPLIT_SECONDS ? Math.min((navigator.hardwareConcurrency || 1) - 1, MOST_WORKERS) : 1);
  if (count < 2) return null;
  const plan = JSON.parse(await call('Ranges', count));
  return plan.ranges.length > 1 ? { count, ...plan } : null;
}

// A run split into ranges: the page's own worker scores the first, a worker of its own each other (it opens the same
// file), and the page's worker merges their scores (Api's ScoreRange, RangeBytes, Merge): a promise of Merge's JSON,
// which is Detect's. Stopped (stop), every worker ends its range where it has got to, and the ranges from the start
// without a gap merge into the result for those frames, as a one-worker run stopped there; a worker that doesn't answer
// is ended and its range left out, unless it is the page's own (a TimeoutError: the run is lost).
async function splitRun(plan, run) {
  const json = JSON.stringify(run);
  let stopped;
  split = { frames: plan.frames, done: plan.ranges.map(() => 0), stopped: new Promise(resolve => { stopped = resolve; }) };
  split.stop = stopped;
  const quiet = () => {};
  helpers = plan.ranges.slice(1).map((_, i) => newWorker({
    progress: ({ done, expected }) => rangeProgress(i + 1, done, expected), shot: quiet, failed: quiet,
  }));
  const scored = plan.ranges.map((_, i) => (async () => {
    const w = i === 0 ? worker : helpers[i - 1];
    if (i > 0) await callOn(w, 'Open', file);
    await callOn(w, 'ScoreRange', json, plan.count, i);
    return callOn(w, 'RangeBytes');
  })());
  let first = false; // the first range has its answer
  scored[0].finally(() => { first = true; });
  for (const p of scored) p.catch(() => {}); // a failure is taken from Promise.all, once
  try {
    let bytes = await Promise.race([Promise.all(scored), split.stopped.then(() => null)]);
    if (bytes === null) {
      // Stopped: as long as a few pauses take at the run's rate, and a second more (answered), for every range.
      const frame = framesDone > 0 ? (performance.now() - started) / framesDone * plan.ranges.length : 0;
      bytes = await Promise.all(scored.map(p => within(p, 1000 + 24 * frame)));
      if (bytes[0] === undefined) {
        replaceWorker();
        throw new DOMException('The page\'s worker didn\'t answer the stop.', 'TimeoutError');
      }
    }
    const joined = new Uint8Array(bytes.reduce((n, b) => n + (b?.length ?? 0), 0));
    let at = 0;
    for (const b of bytes.filter(Boolean)) {
      joined.set(b, at);
      at += b.length;
    }
    return await call('Merge', json, joined, bytes.map(b => b?.length ?? 0));
  } finally {
    split = null;
    endHelpers();
    // A range that failed leaves the page's worker scoring the first: cancelled, before the next call (one worker).
    if (!first) {
      worker.postMessage({ type: 'cancel' });
      await within(scored[0], 1000);
    }
  }
}

// A promise's value within ms: undefined if it takes longer, null if it fails.
function within(promise, ms) {
  let timer;
  const late = new Promise(resolve => { timer = setTimeout(resolve, ms); });
  return Promise.race([promise.catch(() => null), late]).finally(() => clearTimeout(timer));
}

function endHelpers() {
  for (const w of helpers) end(w);
  helpers = [];
}

// A split run's range has done frames of its own (of: how many it has); the status adds them up.
function rangeProgress(index, done, of) {
  if (!runGoes() || !split) return;
  split.done[index] = Math.min(done, of);
  framesDone = split.done.reduce((a, b) => a + b, 0);
  framesExpected = split.frames;
  progress.value = Math.min(framesDone / framesExpected, 1);
  const seconds = (performance.now() - started) / 1000;
  const fps = seconds > 0 ? framesDone / seconds : 0;
  if (state === 'detecting') {
    say(`Frame ${count(framesDone)} of ${count(framesExpected)} · ${split.done.length} workers · ` +
      `${fps.toFixed(0)} frames per second · about ${duration(left(framesDone, framesExpected, seconds, fps))} left`,
      `${estimate} The time left: at this run's rate so far, after its first ${LEFT_AFTER} seconds.`);
  }
  emit('range', { video: opened, index, done: split.done[index], of });
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

// The file's sound, measured once per opened file in a worker of its own alongside its first run (decision
// 2026-10-26-workers.md, "The audio pass"): one pass over the audio packets, which leaves detection's results as they
// are. Not the page's error state if it fails: the run's results stand, and the panels say why there is no sound.
async function listen() {
  if (measured === opened) return;
  const video = measured = opened, quiet = () => {};
  const w = newWorker({ progress: quiet, shot: quiet, failed: quiet });
  listening = { worker: w };
  let audio;
  try {
    await callOn(w, 'Open', file);
    audio = JSON.parse(await callOn(w, 'Audio'));
  } catch (e) {
    if (e.name === 'AbortError') return;
    audio = { notMeasured: e.message, track: null, waveform: null, referenceTone: [], silences: [], peaks: [], loudness: null };
  } finally {
    end(w);
    if (listening?.worker === w) listening = null;
  }
  if (opened !== video) return;
  if ((state === 'done' || state === 'stopped') && shown) say(shown.text, shown.title);
  emit('audio', { video, audio });
}

// How long the run will take, from the frame count and picture size: this browser's own rate from its last run on one
// worker, or the reference rate (REFERENCE_FPS) until it has one; split across workers, SPLIT_SPEEDUP times as fast.
function estimateText(video, workers) {
  const { width, height, frameCount } = video;
  const own = storedRate();
  const rate = (own ?? REFERENCE_PIXELS_PER_SECOND) * (SPLIT_SPEEDUP[workers] ?? 1);
  const from = own ? 'this browser\'s last run' : 'a reference run of Sintel\'s trailer';
  return `Estimate: about ${duration(estimateSeconds(video, workers))} for ${frameCount} frames at ${width}x${height} ` +
    `(${(rate / (width * height)).toFixed(0)} frames per second${workers > 1 ? ` with ${workers} workers` : ''}, ` +
    `from ${from}).`;
}

function estimateSeconds({ width, height, frameCount }, workers = 1) {
  return frameCount * width * height / (storedRate() ?? REFERENCE_PIXELS_PER_SECOND) / (SPLIT_SPEEDUP[workers] ?? 1);
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
  if (split) {
    // Every worker ends its range at its next pause; splitRun merges what they scored from the start.
    say(`Stopping ${split.done.length} workers at ${count(framesDone)} frames…`);
    for (const w of [worker, ...helpers]) w.postMessage({ type: 'stop' });
    split.stop();
    return;
  }
  say(`Stopping at frame ${count(framesDone)}…`);
  const run = running, video = opened;
  worker.postMessage({ type: 'stop' });
  if (await answered(run) || running !== run) return;
  running = null;
  replaceWorker();
  lost(video);
}

// The run is gone with the page's worker, which didn't answer a stop and was replaced.
function lost(video) {
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

function show(r, seconds, workers) {
  setState(r.stopped ? 'stopped' : 'done');
  const rate = `${(r.frameCount / seconds).toFixed(0)} frames per second${workers > 1 ? ` with ${workers} workers` : ''}`;
  const read = r.frameCount * r.frameRate.den / r.frameRate.num;
  shown = {
    text: r.stopped
      ? `Stopped at ${timecode(read)}, frame ${count(r.frameCount)} of ${count(opened.frameCount)}, after ` +
        `${seconds.toFixed(1)} s (${rate})`
      : `Done in ${seconds.toFixed(1)} s, ${rate} (${(read / seconds).toFixed(1)} times real time)`,
    title: `${count(r.frameCount)} frames, ${read.toFixed(3)} s of video, in ${seconds.toFixed(3)} s; ${estimate}`,
  };
  say(listening ? `${shown.text} · measuring the sound…` : shown.text, shown.title);
  // A run of a few seconds is mostly start-up: too short to measure this browser's speed by; a split run's speed is
  // its workers', not this browser's on one.
  if (seconds >= 5 && workers === 1) storeRate(r.frameCount * opened.width * opened.height / seconds);
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
