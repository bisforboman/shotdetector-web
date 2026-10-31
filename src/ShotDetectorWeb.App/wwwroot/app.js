// The page: picks a file, has the worker (worker.js, which owns the .NET runtime) open it and detect, and tells the
// panels what happens. How it is built: docs/decisions/2026-10-24-the-page.md and 2026-10-25-one-page.md. This file
// keeps the page's states (idle, opening, detecting, paused, stopping, done, stopped, lost, error), the landing that
// folds into a toolbar once a file is picked, the one-line status, the options, the estimate, pause and resume, stop,
// the demo and the shot playing under the video (decision 2026-10-26-new-ui.md), a long video split across workers
// and the sound's own worker (decision 2026-10-26-workers.md), a one-worker run's decoder on several threads (decision
// 2026-10-30-threads.md, threads.js) and the service worker that makes that possible and keeps the page for offline use
// (sw.js), and a stopped run kept in this browser to resume, in this visit or a later one (backlog "Resume after Stop,
// across visits"); what else is shown of a file or a run is in the panels
// (panels/<name>.js, listed in index.html), each a lane of the timeline (timeline.js) or a card. A panel imports from
// here:
//   on(event, f)       f(data) on each of the page's events, in this order for a run:
//                        'chose'    { file }                     a new file: whatever was shown of the last one goes
//                        'opened'   { video }                    the file's YuvVideo, from Api's Open (its frames too)
//                        'run'      { video, options, ranges, fallback, from }   a run starts: whatever was shown of
//                                                                the last run goes. ranges: a run split across workers
//                                                                (decision 2026-10-26-workers.md), Api's Ranges'
//                                                                [{ firstFrame, endFrame }], else null; fallback: why a
//                                                                split run is being detected again with one worker;
//                                                                from: the frame a stopped run resumes from (its rest's
//                                                                first), else 0. A kept run shown again starts as a run
//                                                                too, its 'done' a stopped run's
//                        'progress' { video, done, expected }    frames done and expected (0 when unknown), one worker
//                        'range'    { video, index, done, of }   a split run's range: its frames done, of how many
//                        'shot'     { video, shot }              a shot as soon as ShotDetector knows it (a split
//                                                                run's first range's as it scores, the rest at the
//                                                                merge); one told again replaces those from its number
//                                                                on (a resumed run's last, the first range's last)
//                        'done'     { video, result, seconds, name, from, restored }   the run's result (Api's
//                                                                Detect, its CSVs as scenesCsv, a string, and statsCsv,
//                                                                a Blob of text/csv); name: the file's, without its
//                                                                extension, for downloads. A stopped run's too, with
//                                                                result.stopped: the result for its frameCount frames.
//                                                                from: the frame a resumed run's rest started at (the
//                                                                frames before it scored in an earlier run), else 0;
//                                                                restored: a stopped run kept in this browser shown
//                                                                again, which didn't run now (seconds 0)
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
import { RESULTS, ThreadedRunner, threadsUsable } from './threads.js';
import { secondsOf, setDuration, timecode } from './timeline.js';

// The estimate before a run: frames per second scale with picture size (pixels per second about constant). Until this
// browser has finished a run (its own rate, kept in localStorage), the rate is the one measured for Sintel's trailer
// (854x480, the AOT build Pages serves, headless Chromium on a 4-core cloud VM, ManagedCodecs 0.2.3, the median of five
// interleaved rounds, each a first visit, 2026-10-30: 16.7 s): REFERENCE_FPS frames per second at 854x480. GitHub's
// runner goes about 1.8 times as fast (the owner chose the VM's rate). A two-hour film at 640x360 goes a little faster
// per pixel (start-up weighs less): its first estimate was 16% long on that VM with ManagedCodecs 0.1.0's 64 (README
// "Large files").
const REFERENCE_FPS = 75;
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
// What Api's Resume says first when the kept scores don't go on (another version's): the run is detected from the start.
const NOT_RESUMED = 'The stopped run didn\'t resume: ';
// How many times as fast a run goes split across 2, 3 and 4 workers as on one, for the estimate: measured on a
// two-hour film on a 4-core machine (README "Long videos across workers").
const SPLIT_SPEEDUP = { 2: 1.97, 3: 2.93, 4: 3.6 };

// A run on one worker decodes on several threads where the page is cross-origin isolated (decision
// 2026-10-30-threads.md): the threaded runtime on the page's own thread (threads.js) with as many decoder threads as the
// machine has cores, at most MOST_THREADS (6 did no better than 4 on 4 cores). The page's address can say how many
// (?threads=N; 0 or 1: the page's worker, as where the page isn't isolated). How many times as fast as the page's
// worker, for the estimate: measured on the page with Sintel's trailer (854x480, AOT, headless Chromium, a 4-core cloud
// VM, in REFERENCE_FPS's five interleaved rounds, 2026-10-30: 16.7 s, 12.0 s on 2 threads, 11.1 s on 4; 3 in between,
// not measured). At 1280x720 the gain is larger (19.1 s to 10.3 s on 4, decision 2026-10-30-threads.md).
const MOST_THREADS = 4;
const ASKED_THREADS = new URLSearchParams(location.search).has('threads')
  ? Number(new URLSearchParams(location.search).get('threads')) || 1 : null;
const THREADS_SPEEDUP = { 2: 1.39, 3: 1.45, 4: 1.5 };

// ShotDetector's default threshold for each detector, prefilled when one is picked: the one a run without a threshold
// compares against (scenedetect 0.7.2's, DetectionOptions.Compatibility's default; histogram and hash were 0.05 and
// 0.395 in 0.7.1, which ShotDetector's Threshold summary said until 3.21.0). A threshold left at its default is sent
// as none, so the run is the library's own default run. tools/app-check.mjs reads these and checks each against that
// run.
const DEFAULT_THRESHOLDS = { adaptive: 3, content: 27, threshold: 12, histogram: 0.2, hash: 0.35 };
const DEFAULT_MIN_LENGTH = '0.6s';
// The adaptive detector's window (frames on each side averaged, scenedetect's window_width) and minimum content_val
// (DetectionOptions.FrameWindow and MinContentVal), shown while it is picked; at their defaults they are sent as none.
const DEFAULT_WINDOW = 2, DEFAULT_MIN_CONTENT = 15;

const $ = id => document.getElementById(id);
const form = $('form'), fileInput = $('file'), drop = $('drop'), dropText = $('drop-text');
const detector = $('detector'), threshold = $('threshold'), minLength = $('min-length');
const windowInput = $('window'), minContent = $('min-content');
const detectButton = $('detect'), pauseButton = $('pause'), stopButton = $('stop');
const resumeButton = $('resume'), forgetButton = $('forget');
const demoButton = $('demo'), changeButton = $('change');
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
let pausedFor = 0;      // how long it has been paused, in ms, before the pause going on (if any)
let pausedSince = 0;    // when the pause going on started
let framesDone = 0;     // frames the current run has done, as its last progress report said
let framesExpected = 0; // and how many it expects (0 when unknown)
let measured = null;    // the YuvVideo whose sound the panels have been told (Audio once per opened file)
let running = null;     // the current run's Detect call (a promise), while it goes
let runner = null;      // what runs it, one worker: the page's worker, or the threaded runner (threads.js)
let threaded = null;    // the threaded runner, once a run has needed it
let threadedOpen = null; // the YuvVideo the threaded runner has open
let resultOn = null;    // the worker or runner holding the last run's result, which call() asks for it (RESULTS)
let split = null;       // a split run while it goes: { frames, done (each range's), stopped, stop() resolving it }
let helpers = [];       // the workers of a split run's ranges after the first, ended with the run
let listening = null;   // the worker measuring the opened file's sound, until it has
let shown = null;       // what the status says of the last result, { text, title }
let shots = [];         // the shots known, for the caption: told while a run goes, the result's once it is done
let starts = [];        // each one's start in seconds, and its end
let ends = [];
let kept = null;        // the stopped run kept for the chosen file, to resume (KEPT's record), or null
let resumeFrom = 0;     // the frame the current run's rest starts at when it resumes a stopped run, else 0
let resumeAt = 0;       // and the frame the stopped run had got to: the rest scores again from its keyframe (from)

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
  return callOn(RESULTS.has(name) && resultOn ? resultOn : worker, name, ...args);
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
  // A split run's other workers tell no frame they are held at (held): their frames count from their range's keyframe.
  const handlers = { ...fromWorker, progress, shot, ...(progress === fromWorker.progress ? {} : { held: undefined }) };
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
  if (resultOn === w) resultOn = null;
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
    const seconds = runMs() / 1000;
    const fps = seconds > 0 ? (done - resumeFrom) / seconds : 0;
    if (state === 'stopping') say(`Stopping at frame ${count(done)}…`);
    else if (state === 'paused') sayPaused();
    else say(`Frame ${count(done)}${expected > 0 ? ` of ${count(expected)}` : ''} · ${fps.toFixed(0)} frames per second` +
      `${expected > 0 ? ` · about ${duration(left(done, expected, seconds, fps))} left` : ''}`,
      `${estimate} The time left: at this run's rate so far, after its first ${LEFT_AFTER} seconds.`);
    emit('progress', { video: opened, done, expected });
  },
  // Paused, one worker: the frames read where the run is held, which a stop ends it at (the last progress, frames
  // processed, can be up to a pause's 8 frames behind it).
  held({ frames }) {
    if (state !== 'paused' || split) return;
    // A resumed run's frames count from the one before its rest's first.
    framesDone = frames + Math.max(resumeFrom - 1, 0);
    sayPaused();
  },
  // A shot as soon as ShotDetector knows it: a few frames after the cut that ends it with one detector (what the page
  // sends), the same object as in the result's shots (tools/app-check.mjs checks they are the same).
  shot({ shot }) {
    if (!runGoes()) return;
    emit('shot', { video: opened, shot });
    addShot(shot);
  },
};

const runGoes = () => state === 'detecting' || state === 'paused' || state === 'stopping';

// How long the current run has gone, in ms, its pauses left out: the frames-per-second figures, the time left and the
// rate this browser keeps (storeRate) hold still while it is paused.
function runMs() {
  return performance.now() - started - pausedFor - (state === 'paused' ? performance.now() - pausedSince : 0);
}

// Pause and resume (backlog "Pause and resume a run"): the worker's 'pause' holds the run at its next pause (pause.js),
// every worker of a split run's, until 'resume'; nothing is cancelled or ended, so the run goes on where it was and
// its result is the one it would have had. The sound's worker goes on: its pass has no pauses, and is quick.
function pauseOrResume() {
  if (state === 'detecting') {
    pausedSince = performance.now();
    setState('paused');
    for (const w of [runner ?? worker, ...helpers]) w.postMessage({ type: 'pause' });
    sayPaused();
  } else if (state === 'paused') {
    pausedFor += performance.now() - pausedSince;
    setState('detecting');
    for (const w of [runner ?? worker, ...helpers]) w.postMessage({ type: 'resume' });
    say(`Resuming at frame ${count(framesDone)}…`);
  }
}

function sayPaused() {
  // One worker: the frame a stop ends it at (held); split, the ranges' progress added up, which a stop's merge
  // needn't reach.
  const of = framesExpected > 0 ? ` of ${count(framesExpected)}` : '';
  say(`Paused at ${split ? 'about ' : ''}frame ${count(framesDone)}${of}: Resume goes on where it is, Stop shows the ` +
    'result for the frames read.');
}

// The states: idle (a file picked or not), opening, detecting, paused (a run held where it is until resumed), stopping
// (told to stop, its result to come), done, stopped (a stopped run's result, shown as a finished one's), lost (a run
// whose worker didn't answer a stop) and error.
function setState(next) {
  state = next;
  const busy = next === 'opening' || runGoes();
  document.body.dataset.state = next;
  // While a run goes, another file can be chosen: the run is abandoned (choose).
  fileInput.disabled = changeButton.disabled = next === 'opening';
  for (const input of [detector, threshold, minLength, windowInput, minContent]) input.disabled = busy;
  detectButton.disabled = busy || !file;
  demoButton.disabled = busy;
  detectButton.hidden = busy;
  // Opening can't be stopped half-way, only abandoned: the worker is terminated (stop).
  pauseButton.hidden = next !== 'detecting' && next !== 'paused';
  pauseButton.textContent = next === 'paused' ? 'Resume' : 'Pause';
  stopButton.hidden = !busy;
  stopButton.disabled = next === 'stopping';
  stopButton.textContent = next === 'opening' ? 'Cancel' : 'Stop';
  resumeButton.hidden = forgetButton.hidden = busy || !keptFor(file);
  // With a stopped run to resume, Resume is the button to press; Detect starts over.
  detectButton.classList.toggle('quiet', !resumeButton.hidden);
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
    const run = running, w = runner ?? worker;
    running = null;
    w.postMessage({ type: 'cancel' });
    endHelpers();
    if (!await answered(run)) replaceRunner(w);
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
  unplayable.hidden = true;
  video.hidden = false;
  video.src = URL.createObjectURL(file);
  say('');
  // A stopped run of this file kept from this visit or an earlier one: shown at once, its options in the form.
  if (!keptFor(chosen)) kept = null;
  setState('idle');
  const record = await readKept();
  if (file !== chosen || state !== 'idle') return false;
  if (keptFor(chosen, record)) kept = record;
  if (!keptFor(chosen)) return false;
  start('restore');
  return true;
}

// Opens the file if the current worker hasn't, then detects ('detect'), resumes the kept run ('resume') or shows it
// again ('restore'). Errors are the library's messages; a call that a cancel or a replaced worker took away is no error.
async function start(how = 'detect') {
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
    if (how === 'restore') await restore(kept);
    else await detect(how === 'resume' ? kept : null);
  } catch (e) {
    if (e.name !== 'AbortError') fail(e.message);
  }
}

// One run on the opened file, split across workers for a long video (ranges), the sound measured alongside it the
// first time; or the rest of a stopped run (resumed: the kept record), with the options it ran with, split as a run of
// the rest's length would be. Stop (stop) ends it early with the result for the frames read, which is shown as a
// finished run's is; a new file (choose) abandons it.
async function detect(resumed = null) {
  setState('detecting');
  if (resumed) setForm(resumed.options);
  const run = resumed?.options ?? options(), plan = await ranges(opened, resumed?.from ?? 0);
  listen();
  await go(run, plan, null, resumed);
}

// The run itself: on one worker (Api's Detect) or split (plan: splitRun). A split run whose ranges don't merge, or one of
// whose workers fails, is detected again with one worker, saying why (fallback).
// A resumed run (resumed: the kept record) goes on from its kept scores (Api's Resume, or Merge with them); kept scores
// that don't resume (another version's, say) are forgotten and the run detected from the start, saying why (restarted).
async function go(run, plan, fallback = null, resumed = null, restarted = null) {
  const from = resumeFrom = resumed?.from ?? 0;
  resumeAt = resumed?.frames ?? 0;
  framesDone = from;
  framesExpected = 0;
  // A resume on one worker tells the shots of the rest from the stopped part's last, cut short at the stop: the ones
  // before it stay. Split, the merge tells them all.
  const before = resumed && !plan ? resumed.shots.slice(0, -1) : [];
  const keptBytes = resumed ? new Uint8Array(await resumed.blob.arrayBuffer()) : null;
  emit('run', { video: opened, options: run, ranges: plan?.ranges ?? null, fallback, from });
  clearShots();
  for (const shot of before) {
    emit('shot', { video: opened, shot });
    addShot(shot);
  }
  result.hidden = false;
  progress.removeAttribute('value');
  const workers = plan?.ranges.length ?? 1, threads = plan ? 1 : threadCount();
  const frames = Math.max(opened.frames - from, 0);
  estimate = estimateText(opened, workers, threads, frames);
  estimated = estimateSeconds(opened, workers, threads, frames);
  const doing = fallback ? `${fallback} ${resumed ? 'Resuming' : 'Detecting again'} with one worker`
    : resumed ? `Resuming ${resumedFrom()}` : restarted ? `${restarted} Detecting from the start` : 'Detecting';
  say(`${doing} · about ${duration(estimated)} for ${count(frames)} frames${workers > 1 ? ` with ${workers} workers` : ''}` +
    `${threads > 1 ? ` on ${threads} threads` : ''}`, estimate);
  started = performance.now();
  pausedFor = 0;
  runner = worker;
  const video = opened;
  const detecting = running = plan ? splitRun(plan, run, keptBytes, from) : oneRun(run, threads, keptBytes);
  let json;
  try {
    json = await detecting;
  } catch (e) {
    if (running !== detecting) return; // abandoned: a new file was chosen
    if (e.name === 'TimeoutError') return lost(video);
    if (resumed && e.message?.startsWith(NOT_RESUMED) && state !== 'stopping') {
      running = null;
      forget();
      return go(run, await ranges(opened), null, null, e.message);
    }
    if (!plan || state === 'stopping' || e.name === 'AbortError') throw e;
    running = null;
    return go(run, null, e.message.startsWith(NOT_MERGED) ? e.message : `A worker failed: ${e.message}`, resumed);
  }
  const r = JSON.parse(json);
  const seconds = runMs() / 1000;
  if (running !== detecting) return; // abandoned: a new file was chosen
  running = null;
  // The CSVs come of their own, not inside Detect's JSON (README "Large files"): the scenes CSV a string, the stats CSV
  // a Blob .NET wrote in parts (README "What the heap holds").
  const by = resultOn = runner ?? worker;
  r.scenesCsv = await callOn(by, 'ScenesCsv');
  r.statsCsv = await callOn(by, 'StatsCsv');
  if (opened !== video) return; // a new file was chosen meanwhile
  show(r, seconds, workers, by === threaded ? r.threads : 1);
  // Stopped: kept to resume, in this visit or a later one; finished: the kept run of these options is done with.
  if (r.stopped) await keep(by, run, r);
  else if (keptFor(file) && JSON.stringify(kept.options) === JSON.stringify(run)) forget();
}

// A stopped run kept in this browser to resume (the owner's choices, 2026-10-30: only the last one, of any file; shown
// at once when its file is picked again, its options in the form): IndexedDB's KEPT store, one record under KEPT_KEY,
// { name, size, lastModified, options, blob, frames, from, shots, saved }: the file (by name, size and modified time),
// the options it ran with, its scores as Api's Kept gives them (a Blob, whose first four bytes are the frame its rest
// starts at: from), the frames its result read and its shots (which a resume shows while the rest's are told), and
// when. Nothing leaves the browser. Storage can be off or fail (a
// private window): the run then resumes only in this visit, from the record kept in memory.
const KEPT = 'kept', KEPT_KEY = 'last';

function keptStore(mode, f) {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open('shotdetector-web', 1);
    open.onupgradeneeded = () => open.result.createObjectStore(KEPT);
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result, t = db.transaction(KEPT, mode), request = f(t.objectStore(KEPT));
      t.oncomplete = () => { db.close(); resolve(request.result); };
      t.onerror = t.onabort = () => { db.close(); reject(t.error); };
    };
  });
}

async function readKept() {
  try { return await keptStore('readonly', s => s.get(KEPT_KEY)) ?? null; } catch { return null; }
}

// Whether record (the kept run, unless said) is of file f.
function keptFor(f, record = kept) {
  return !!record && !!f && record.name === f.name && record.size === f.size && record.lastModified === f.lastModified;
}

async function keep(by, run, r) {
  const video = opened, chosen = file;
  let blob;
  try {
    blob = await callOn(by, 'Kept');
  } catch {
    return; // the runner went: nothing to keep
  }
  const from = new DataView(await blob.slice(0, 4).arrayBuffer()).getInt32(0, true);
  if (opened !== video || from < 0) return;
  kept = { name: chosen.name, size: chosen.size, lastModified: chosen.lastModified, options: run, blob,
    frames: r.frameCount, from, shots: r.shots, saved: Date.now() };
  setState(state);
  const record = kept;
  try { await keptStore('readwrite', s => s.put(record, KEPT_KEY)); } catch { /* kept for this visit only */ }
}

function forget() {
  kept = null;
  setState(state);
  keptStore('readwrite', s => s.delete(KEPT_KEY)).catch(() => {});
}

// The kept run shown again (a later visit, its file picked): Api's Merge of its scores alone, the stopped run's result,
// shown as a stopped run's with Resume beside Detect. Scores that don't merge any more are forgotten. The file's sound is
// measured as it would be for a run (listen).
async function restore(record) {
  const video = opened;
  setForm(record.options);
  setState('opening');
  progress.removeAttribute('value');
  say('Showing the stopped run kept in this browser…');
  const bytes = new Uint8Array(await record.blob.arrayBuffer());
  let json;
  try {
    json = await call('Merge', JSON.stringify(record.options), bytes, new Uint8Array(0), []);
  } catch (e) {
    if (e.name === 'AbortError') throw e;
    forget();
    setState('idle');
    say(`The stopped run kept in this browser didn't show, so it was forgotten: ${e.message}`);
    return;
  }
  if (opened !== video || kept !== record) return;
  // The sound is the file's, not the run's: measured alongside, as for a run.
  listen();
  emit('run', { video, options: record.options, ranges: null, fallback: null, from: 0 });
  clearShots();
  result.hidden = false;
  const r = JSON.parse(json);
  resultOn = worker;
  r.scenesCsv = await call('ScenesCsv');
  r.statsCsv = await call('StatsCsv');
  if (opened !== video) return;
  resumeFrom = resumeAt = 0;
  show(r, 0, 1, 1, record);
}

// The form showing a run's options (as options() sends them: a default as none).
function setForm(run) {
  detector.value = run.detector ?? 'adaptive';
  form.dataset.detector = detector.value;
  threshold.value = run.threshold ?? DEFAULT_THRESHOLDS[detector.value];
  minLength.value = run.minSceneLength ?? DEFAULT_MIN_LENGTH;
  windowInput.value = run.frameWindow ?? DEFAULT_WINDOW;
  minContent.value = run.minContentVal ?? DEFAULT_MIN_CONTENT;
}

// A run on one worker: Api's Detect's JSON, with threads (how many the decoder had). On threads where the page can
// (threadCount), on the threaded runner (threads.js), which opens the file for itself first; on the page's worker if the
// threaded runtime doesn't start.
// A resumed run (keptBytes: its kept scores) is Api's Resume.
async function oneRun(run, threads, keptBytes = null) {
  const json = JSON.stringify(run), args = keptBytes ? ['Resume', json, keptBytes] : ['Detect', json];
  if (threads > 1) {
    const r = await threadedRunner(threads);
    if (r) {
      runner = r;
      const detecting = callOn(r, ...args);
      // Stopped while the threaded runtime opened the file: the run stops at its first pause.
      if (state === 'stopping') r.postMessage({ type: 'stop' });
      return JSON.stringify({ ...JSON.parse(await detecting), threads });
    }
    // Paused while the threaded runtime didn't start: the page's worker holds the run instead.
    if (state === 'paused') worker.postMessage({ type: 'pause' });
  }
  return call(...args);
}

// How many threads a one-worker run's decoder has: ?threads=N, else the machine's cores, at most MOST_THREADS; 1 (the
// page's worker) where the page isn't cross-origin isolated or the threaded runtime was given up on.
function threadCount() {
  const n = ASKED_THREADS ?? Math.min(navigator.hardwareConcurrency || 1, MOST_THREADS);
  return n >= 2 && threadsUsable() ? n : 1;
}

// The threaded runner with the opened file open in it; null if the threaded runtime doesn't start (then threads are off
// until a reload: threadsUsable).
async function threadedRunner(threads) {
  if (!threaded) {
    const r = threaded = new ThreadedRunner(threads);
    r.onmessage = ({ data }) => { if (!ended.has(r)) fromWorker[data.type]?.(data); };
    // Its runtime ended: its calls fail with why (the run's, shown as the page's error), and threads are off.
    r.onerror = ({ message }) => {
      if (threaded === r) threaded = threadedOpen = null;
      end(r, new Error(message));
    };
  }
  if (threadedOpen !== opened) {
    try {
      await callOn(threaded, 'Open', file);
      threadedOpen = opened;
    } catch (e) {
      if (threadsUsable()) throw e;
      return null;
    }
  }
  return threaded;
}

// Ends the runner that didn't answer: the page's worker is replaced; the threaded runner is abandoned, and runs go on
// the page's worker until a reload (threads.js).
function replaceRunner(w) {
  if (w === threaded) {
    end(threaded);
    threaded = threadedOpen = null;
  } else {
    replaceWorker();
  }
}

// How a run of the opened file is split (decision 2026-10-26-workers.md): above SPLIT_SECONDS of video, into as many
// ranges as there are cores but one, at most MOST_WORKERS (or as ?workers= says), each from a keyframe (Api's Ranges):
// { count, frames, ranges }, or null for one worker (a short video, a machine with two cores, too few keyframes). A
// resumed run's rest from frame from, by its own length.
async function ranges(video, from = 0) {
  const length = lengthOf(video) * Math.max(video.frames - from, 0) / video.frames;
  const count = ASKED_WORKERS ??
    (length > SPLIT_SECONDS ? Math.min((navigator.hardwareConcurrency || 1) - 1, MOST_WORKERS) : 1);
  if (count < 2) return null;
  const plan = JSON.parse(await call('Ranges', count, from));
  return plan.ranges.length > 1 ? { count, ...plan } : null;
}

// A run split into ranges: the page's own worker scores the first, a worker of its own each other (it opens the same
// file), and the page's worker merges their scores (Api's ScoreRange, RangeBytes, Merge): a promise of Merge's JSON,
// which is Detect's. Stopped (stop), every worker ends its range where it has got to, and the ranges from the start
// without a gap merge into the result for those frames, as a one-worker run stopped there; a worker that doesn't answer
// is ended and its range left out, unless it is the page's own (a TimeoutError: the run is lost).
// A resumed run's rest (from its first frame, from) merges with its kept scores (keptBytes).
async function splitRun(plan, run, keptBytes = null, from = 0) {
  const json = JSON.stringify(run);
  let stopped;
  split = { frames: plan.frames, from, done: plan.ranges.map(() => 0), stopped: new Promise(resolve => { stopped = resolve; }) };
  split.stop = stopped;
  const quiet = () => {};
  helpers = plan.ranges.slice(1).map((_, i) => newWorker({
    progress: ({ done, expected }) => rangeProgress(i + 1, done, expected), shot: quiet, failed: quiet,
  }));
  const scored = plan.ranges.map((_, i) => (async () => {
    const w = i === 0 ? worker : helpers[i - 1];
    if (i > 0) await callOn(w, 'Open', file);
    await callOn(w, 'ScoreRange', json, plan.count, i, from);
    return callOn(w, 'RangeBytes');
  })());
  let first = false; // the first range has its answer
  scored[0].finally(() => { first = true; });
  for (const p of scored) p.catch(() => {}); // a failure is taken from Promise.all, once
  try {
    let bytes = await Promise.race([Promise.all(scored), split.stopped.then(() => null)]);
    if (bytes === null) {
      // Stopped: as long as a few pauses take at the run's rate, and a second more (answered), for every range.
      const frame = framesDone > from ? runMs() / (framesDone - from) * plan.ranges.length : 0;
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
    return await call('Merge', json, keptBytes ?? new Uint8Array(0), joined, bytes.map(b => b?.length ?? 0));
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
  framesDone = split.from + split.done.reduce((a, b) => a + b, 0);
  framesExpected = split.frames;
  progress.value = Math.min(framesDone / framesExpected, 1);
  const seconds = runMs() / 1000;
  const fps = seconds > 0 ? (framesDone - split.from) / seconds : 0;
  if (state === 'paused') sayPaused();
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
  const frame = framesDone > resumeFrom ? runMs() / (framesDone - resumeFrom) : 0;
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
// worker (on as many threads), or the reference rate (REFERENCE_FPS) until it has one; split across workers,
// SPLIT_SPEEDUP times as fast, on threads THREADS_SPEEDUP times (unless it has a rate of its own on them).
// frames: the run's (a resumed run's rest), else the video's.
function estimateText(video, workers, threads = 1, frames = video.frames) {
  const { width, height } = video;
  const { rate, own } = rateFor(workers, threads);
  const from = own ? 'this browser\'s last run' : 'a reference run of Sintel\'s trailer';
  return `Estimate: about ${duration(estimateSeconds(video, workers, threads, frames))} for ${frames} frames at ` +
    `${width}x${height} (${(rate / (width * height)).toFixed(0)} frames per second` +
    `${workers > 1 ? ` with ${workers} workers` : ''}${threads > 1 ? ` on ${threads} threads` : ''}, from ${from}).`;
}

function estimateSeconds({ width, height, frames: shown }, workers = 1, threads = 1, frames = shown) {
  return frames * width * height / rateFor(workers, threads).rate;
}

// Pixels per second for a run, and whether it is this browser's own.
function rateFor(workers, threads) {
  const threadsOwn = threads > 1 ? storedRate(threads) : null;
  if (threadsOwn) return { rate: threadsOwn, own: true };
  const own = storedRate(1);
  return {
    rate: (own ?? REFERENCE_PIXELS_PER_SECOND) * (SPLIT_SPEEDUP[workers] ?? 1) * (THREADS_SPEEDUP[threads] ?? 1),
    own: own !== null,
  };
}

// The time a run has left: at its own rate once it has gone LEFT_AFTER seconds (before, start-up weighs on the rate
// too much), the estimate's until then.
const LEFT_AFTER = 3;
function left(done, expected, seconds, fps) {
  return seconds >= LEFT_AFTER && fps > 0 ? (expected - done) / fps : Math.max(estimated - seconds, 0);
}

// The pixels per second of this browser's last finished run on one worker on as many threads, if it kept one (storage
// can be off or throw).
const rateKey = threads => threads > 1 ? `${RATE_KEY}On${threads}Threads` : RATE_KEY;
function storedRate(threads = 1) {
  try {
    const rate = Number(localStorage.getItem(rateKey(threads)));
    return rate > 0 ? rate : null;
  } catch {
    return null;
  }
}

function storeRate(rate, threads = 1) {
  try { localStorage.setItem(rateKey(threads), String(rate)); } catch { /* the reference rate stays */ }
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
  const run = {
    detector: kind,
    threshold: t === '' || Number(t) === DEFAULT_THRESHOLDS[kind] ? null : Number(t),
    minSceneLength: m === '' || m === DEFAULT_MIN_LENGTH ? null : m,
  };
  if (kind === 'adaptive') {
    const w = windowInput.value.trim(), c = minContent.value.trim();
    run.frameWindow = w === '' || Number(w) === DEFAULT_WINDOW ? null : Number(w);
    run.minContentVal = c === '' || Number(c) === DEFAULT_MIN_CONTENT ? null : Number(c);
  }
  return run;
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
  if (state !== 'detecting' && state !== 'paused') return;
  // Stopped while paused: the worker's 'stop' also lets the held run go on, to end at the pause it waits at.
  if (state === 'paused') pausedFor += performance.now() - pausedSince;
  setState('stopping');
  if (split) {
    // Every worker ends its range at its next pause; splitRun merges what they scored from the start.
    say(`Stopping ${split.done.length} workers at ${count(framesDone)} frames…`);
    for (const w of [worker, ...helpers]) w.postMessage({ type: 'stop' });
    split.stop();
    return;
  }
  say(`Stopping at frame ${count(framesDone)}…`);
  const run = running, video = opened, w = runner ?? worker;
  w.postMessage({ type: 'stop' });
  if (await answered(run) || running !== run) return;
  running = null;
  const onThreads = w === threaded;
  replaceRunner(w);
  lost(video, onThreads);
}

// The run is gone with the page's worker, which didn't answer a stop and was replaced; or with the threaded runner,
// which was given up on (threads stay off until a reload).
function lost(video, onThreads = false) {
  setState('lost');
  say(`Lost at frame ${count(framesDone)}: ${onThreads
    ? 'the threaded run didn\'t answer the stop, so it was abandoned (the next runs go on one thread), and its'
    : 'the worker didn\'t answer the stop, so it was restarted, and the run\'s'} result went with it.`);
  emit('cancelled', { video, done: framesDone, expected: framesExpected });
  follow();
}

function fail(message) {
  setState('error');
  say('');
  error.textContent = message;
}

// restored: the kept record of a stopped run shown again (restore), not a run of this visit.
function show(r, seconds, workers, threads, restored = null) {
  setState(r.stopped ? 'stopped' : 'done');
  // A resumed run read its rest's frames (and those it scored again from a keyframe), not the stopped part's: its speed
  // and its times real time are of those (as the shot statistics' Run).
  const ran = r.frameCount - resumeFrom, ranSeconds = ran * r.frameRate.den / r.frameRate.num;
  const rate = `${(ran / seconds).toFixed(0)} frames per second${workers > 1 ? ` with ${workers} workers` : ''}` +
    `${threads > 1 ? ` on ${threads} threads` : ''}`;
  const read = r.frameCount * r.frameRate.den / r.frameRate.num, at = `${timecode(read)}, frame ${count(r.frameCount)} ` +
    `of ${count(opened.frames)}`;
  shown = restored ? {
    text: `Stopped at ${at}, kept in this browser since ${new Date(restored.saved).toLocaleString()}: Resume goes on ` +
      'from there, Detect starts over',
    title: `${count(r.frameCount)} frames, ${read.toFixed(3)} s of video, scored before; the scores stay in this ` +
      'browser (IndexedDB) until the run is finished, another one is stopped, or Forget',
  } : {
    text: r.stopped
      ? `Stopped at ${at}, after ${seconds.toFixed(1)} s (${rate}): Resume goes on from there`
      : `Done in ${seconds.toFixed(1)} s, ${rate} (${(ranSeconds / seconds).toFixed(1)} times real time)` +
        `${resumeFrom > 0 || resumeAt > 0 ? `, resumed ${resumedFrom()}` : ''}`,
    title: `${count(ran)} frames, ${ranSeconds.toFixed(3)} s of video, in ${seconds.toFixed(3)} s` +
      `${resumeAt > resumeFrom ? `: from frame ${count(resumeFrom)}, the keyframe before the stop, where the rest ` +
        'scores again for the frames that need those before them' : ''}; ${estimate}`,
  };
  say(listening ? `${shown.text} · measuring the sound…` : shown.text, shown.title);
  // A run of a few seconds is mostly start-up: too short to measure this browser's speed by; a split run's speed is
  // its workers', not this browser's on one.
  if (!restored && seconds >= 5 && workers === 1) storeRate(ran * opened.width * opened.height / seconds, threads);
  emit('done', { video: opened, result: r, seconds, name: file.name.replace(/\.[^.]*$/, ''), from: resumeFrom,
    restored: !!restored });
  clearShots();
  for (const shot of r.shots) addShot(shot);
  result.hidden = false;
}

// Where a resumed run went on: the frame its stopped part had got to, and the keyframe before it its rest scored again
// from (Api's Kept: the last range start at or before the last frame scored), when that is earlier.
function resumedFrom() {
  return `from frame ${count(resumeAt || resumeFrom)}` +
    `${resumeAt > resumeFrom ? ` (rescored from its keyframe, frame ${count(resumeFrom)})` : ''}`;
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
  // A resumed run tells the stopped part's last shot again, to where it ends: it replaces the one cut short.
  if (shot.shot <= shots.length) {
    shots.length = starts.length = ends.length = shot.shot - 1;
  }
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
const lengthOf = v => v.duration ?? v.frames * v.frameRate.den / v.frameRate.num;

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

detector.onchange = () => { threshold.value = DEFAULT_THRESHOLDS[detector.value]; form.dataset.detector = detector.value; };
threshold.value = DEFAULT_THRESHOLDS[detector.value];
form.dataset.detector = detector.value;
fileInput.onchange = () => choose(fileInput.files[0]);
changeButton.onclick = () => fileInput.click();
// The whole drop zone picks a file, not only its label: all but the demo's line.
drop.onclick = e => { if (e.target === drop) fileInput.click(); };
for (const type of ['timeupdate', 'seeking', 'emptied', 'play', 'pause']) video.addEventListener(type, follow);
// A file the browser can't play (MJPEG in AVI: decision 2026-10-30-mjpeg-avi.md) is analysed all the same: a line in the
// player's place says so.
const unplayable = $('unplayable');
video.addEventListener('error', () => {
  if (!video.error || video.error.code === MediaError.MEDIA_ERR_ABORTED) return;
  video.hidden = true;
  unplayable.hidden = false;
});
form.onsubmit = e => { e.preventDefault(); start(); };
stopButton.onclick = stop;
resumeButton.onclick = () => start('resume');
forgetButton.onclick = () => {
  forget();
  if (state === 'stopped') say(`${shown.text.replace(/: Resume goes on.*$/, '')} · forgotten`, shown.title);
};
pauseButton.onclick = pauseOrResume;
// The demo clip, next to the page (pages.yml), run as if it had been picked.
demoButton.onclick = async () => {
  demoButton.disabled = true;
  say('Downloading the demo clip…');
  try {
    const response = await fetch('demo/sintel-trailer.mp4');
    if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
    // Its modified time the server's, so that a run of it stopped is the same file's on a later visit.
    const lastModified = Date.parse(response.headers.get('last-modified')) || 0;
    if (!await choose(new File([await response.blob()], 'sintel-trailer.mp4', { type: 'video/mp4', lastModified }))) start();
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

// The service worker (sw.js): isolation for the threads, and the page offline. A first visit isn't controlled by it, so
// not isolated: once it takes over, the page reloads, once a session, if nothing has been picked yet (else threads come
// with the next visit). Without service workers (some private modes, file:) the page works as before, on one thread.
async function serve() {
  if (!('serviceWorker' in navigator)) return;
  try {
    await navigator.serviceWorker.register('sw.js');
  } catch {
    return;
  }
  if (globalThis.crossOriginIsolated || navigator.serviceWorker.controller) return;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (globalThis.crossOriginIsolated || file || state !== 'idle') return;
    try {
      if (sessionStorage.getItem('isolating')) return;
      sessionStorage.setItem('isolating', '1');
    } catch {
      return; // no way to know it reloaded already: not at all, rather than over and over
    }
    location.reload();
  });
}

serve();
replaceWorker();
setState('idle');
