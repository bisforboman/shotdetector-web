// shotdetector-web: shot detection in the visitor's browser, the same shots, CSVs and timeline files as ShotDetector's
// FFmpeg path, byte for byte (decision docs/decisions/2026-11-02-npm-package.md). This module is the package's public
// API. Under it is the App's own worker (worker.js: the .NET runtime, each export called by name), the same file the
// page runs, and for threads the page's threaded runner (threads.js). Nothing here is the page's.
//
// The package's files are served as they are, from `base` (default: this module's own folder):
//   aot/ and interpreted/   worker.js, its imports and _framework/ (one bundle each: the site picks, `bundle`)
//   threads.js, threads/    the threaded AOT bundle, on the page's thread, only where crossOriginIsolated (`threads`)

const BUNDLES = ['aot', 'interpreted'];
const OPTION_KEYS = ['detector', 'threshold', 'minSceneLength', 'frameWindow', 'minContentVal', 'deinterlace', 'detectors'];
const TIMELINES = ['edl', 'fcpxml', 'fcp7', 'otio', 'qp', 'keyframes', 'shotchanges'];
const STOP_MS = 10000;  // a run that hasn't answered a stop by then is ended, as the page ends a worker that doesn't
const SEEK_MS = 5000;   // a still whose seek hasn't answered by then is null

/** Shot detection on one file at a time, in a worker of its own (or on threads), kept until close(). */
export class ShotDetector {
  constructor({ base = new URL('./', import.meta.url), bundle = 'aot', threads = 0 } = {}) {
    if (!BUNDLES.includes(bundle)) throw new TypeError(`bundle: one of ${BUNDLES.join(', ')}, not ${bundle}`);
    this.base = new URL(base, globalThis.location?.href);
    this.bundle = bundle;
    this.threadsAsked = threads;
    this.runner = null;     // the worker (or threaded runner) the file is open in
    this.file = null;
    this.video = null;      // Open's description
    this.threads = 1;       // the decoder's threads of the runner in use
    this.lastId = 0;
    this.calls = new Map(); // id -> { resolve, reject }
    this.listeners = null;  // the running detect's { progress, shot, held }
    this.closed = false;
  }

  /** Opens a file (a File or Blob): its description as FFmpeg gives it, or an Error saying why it is refused. */
  async open(file) {
    this.#live();
    this.#end();
    this.file = file;
    this.threads = 1;
    if (this.threadsAsked > 1 && globalThis.crossOriginIsolated === true && !await isAvi(file)) {
      const { ThreadedRunner, threadsUsable } = await import(new URL('threads.js', this.base).href);
      if (threadsUsable()) {
        this.#listen(new ThreadedRunner(this.threadsAsked));
        try {
          this.video = JSON.parse(await this.#call('Open', file));
          this.threads = this.threadsAsked;
          return this.video;
        } catch (e) {
          if (threadsUsable()) throw e;
          this.#end(); // the threaded runtime didn't start: the worker instead
        }
      }
    }
    this.#listen(this.#worker());
    this.video = JSON.parse(await this.#call('Open', file));
    return this.video;
  }

  /**
   * Detects the opened file's shots. Returns a Run: a promise of { shots, frames, frameRate, duration, stopped,
   * warnings, threads } with stop(), pause(), resume() and cancel().
   */
  detect(options = {}) {
    this.#live();
    if (!this.runner) throw new Error('No file open: call open(file) first.');
    if (this.listeners) throw new Error('A run is already going on this detector.');
    const runner = this.runner, request = {};
    for (const key of OPTION_KEYS) if (options[key] != null) request[key] = options[key];
    let stopTimer = null;
    const listeners = this.listeners = {
      progress: ({ done, expected }) => options.onProgress?.({ done, expected }),
      shot: ({ shot }) => options.onShot?.(shot),
    };
    const answer = this.#call('Detect', JSON.stringify(request));
    const result = answer.then(json => {
      const r = JSON.parse(json);
      return { shots: r.shots, frames: r.frameCount, frameRate: r.frameRate, duration: r.duration ?? null,
        stopped: r.stopped, warnings: r.warnings, threads: this.threads };
    }).finally(() => {
      clearTimeout(stopTimer);
      if (this.listeners === listeners) this.listeners = null;
      options.signal?.removeEventListener('abort', abort);
    });
    const run = new Run(result);
    run.stop = () => {
      if (this.listeners !== listeners) return;
      runner.postMessage({ type: 'stop' });
      stopTimer ??= setTimeout(() => this.#lose(runner, 'The run didn\'t answer the stop, so its worker was ended.'),
        STOP_MS);
    };
    run.pause = () => { if (this.listeners === listeners) runner.postMessage({ type: 'pause' }); };
    run.resume = () => { if (this.listeners === listeners) runner.postMessage({ type: 'resume' }); };
    run.cancel = () => { if (this.listeners === listeners) runner.postMessage({ type: 'cancel' }); };
    const abort = () => run.cancel();
    if (options.signal?.aborted) abort();
    else options.signal?.addEventListener('abort', abort);
    return run;
  }

  /** The last run's scenes CSV (scenedetect list-scenes'), a string. */
  scenesCsv() { return this.#result('ScenesCsv'); }
  /** The last run's stats CSV (scenedetect -s'), a Blob. */
  statsCsv() { return this.#result('StatsCsv'); }
  /** The last run's shots as ShotDetector's JSON, a string. */
  shotsJson() { return this.#result('ShotsJson'); }
  /** The last run's shots as scenedetect's save-html page without images, a string. */
  shotsHtml() { return this.#result('ShotsHtml'); }
  /** The last run's shots in an editor's format (edl, fcpxml, fcp7, otio, qp, keyframes, shotchanges), a string. */
  timeline(format, fileName = this.file?.name ?? 'video.mp4') {
    if (!TIMELINES.includes(format)) throw new TypeError(`format: one of ${TIMELINES.join(', ')}, not ${format}`);
    return this.#result('TimelineFile', format, fileName);
  }
  /** The last run's YouTube chapters at least `minimum` seconds apart: { minimum, starts, text, notMade }. */
  async chapters(minimum = 30) { return JSON.parse(await this.#result('YouTubeChapters', minimum)); }

  /**
   * A still per shot (a Blob, image/jpeg, or null where the browser can't show the frame): the browser's decode of the
   * shot's middle (or first) frame, not ShotDetector's; an AVI's from its own JPEGs. Needs a document.
   */
  async stills(shots, { at = 'middle', width = 320, quality = 0.85 } = {}) {
    this.#live();
    if (!this.file) throw new Error('No file open: call open(file) first.');
    const rate = this.video?.frameRate, frame = rate ? rate.den / rate.num : 0;
    const times = shots.map(s => {
      const start = seconds(s.startTimecode), t = at === 'first' ? start : (start + seconds(s.endTimecode)) / 2;
      return frame ? (Math.floor(t / frame + 1e-9) + 0.5) * frame : t;
    });
    const jpegs = await isAvi(this.file) ? await this.#jpegs() : null;
    return jpegs ? fromJpegs(jpegs, times, width, quality) : fromVideo(this.file, times, width, quality);
  }

  /** Ends the worker. The detector can't be used after. */
  close() {
    this.closed = true;
    this.#end();
    this.file = this.video = null;
  }

  #live() { if (this.closed) throw new Error('This ShotDetector is closed.'); }

  #worker() {
    const url = new URL(`${this.bundle}/worker.js`, this.base);
    if (url.origin === globalThis.location?.origin || !globalThis.location) return new Worker(url, { type: 'module' });
    // A worker can't be started from another origin (a CDN): a module of this origin that imports it.
    const shim = URL.createObjectURL(new Blob([`import ${JSON.stringify(url.href)};`], { type: 'text/javascript' }));
    try { return new Worker(shim, { type: 'module' }); } finally { setTimeout(() => URL.revokeObjectURL(shim), 60000); }
  }

  #listen(runner) {
    this.runner = runner;
    runner.onmessage = ({ data }) => {
      if (this.runner !== runner) return;
      const call = this.calls.get(data.id);
      switch (data.type) {
        case 'result': this.calls.delete(data.id); call?.resolve(data.value); break;
        case 'error': this.calls.delete(data.id); call?.reject(new Error(data.message)); break;
        case 'cancelled': this.calls.delete(data.id); call?.reject(abortError()); break;
        default: this.listeners?.[data.type]?.(data);
      }
    };
    runner.onerror = e => {
      e.preventDefault?.();
      this.#lose(runner, `The worker stopped: ${e.message || 'unknown error'}`);
    };
  }

  #call(name, ...args) {
    return new Promise((resolve, reject) => {
      const id = ++this.lastId;
      this.calls.set(id, { resolve, reject });
      this.runner.postMessage({ type: 'call', id, name, args });
    });
  }

  #result(name, ...args) {
    this.#live();
    if (!this.runner) throw new Error('No run to give: call open(file) and detect() first.');
    return this.#call(name, ...args);
  }

  // The worker ended (or the threaded runner abandoned): its calls fail with why; the file has to be opened again.
  #lose(runner, message) {
    if (this.runner !== runner) return;
    this.#end(new Error(message));
  }

  #end(error = abortError()) {
    const runner = this.runner;
    this.runner = null;
    this.listeners = null;
    runner?.terminate();
    for (const { reject } of this.calls.values()) reject(error);
    this.calls.clear();
  }

  async #jpegs() {
    const { timeBase: { num, den }, pts, offsets, sizes } = JSON.parse(await this.#result('Packets'));
    if (!offsets) return null;
    return { file: this.file, times: pts.map(p => (p - pts[0]) * num / den), offsets, sizes };
  }
}

/** A run: a promise of its result, which stop(), pause(), resume() and cancel() act on. */
export class Run extends Promise {
  constructor(executor) {
    if (typeof executor === 'function') super(executor);
    else super((resolve, reject) => executor.then(resolve, reject));
  }
  static get [Symbol.species]() { return Promise; }
}

/**
 * One call: the file opened, detected, the outputs asked for fetched, the stills taken, the worker ended. Returns a
 * Run: a promise of { video, shots, stills, frames, frameRate, duration, stopped, warnings, threads, files }.
 */
export function detectShots(file, options = {}) {
  const { base, bundle, threads, stills = 'middle', stillWidth = 320, outputs = [] } = options;
  const detector = new ShotDetector({ base, bundle, threads });
  let inner = null, stopped = false, paused = false, cancelled = false;
  const result = (async () => {
    try {
      const video = await detector.open(file);
      if (cancelled || options.signal?.aborted) throw abortError();
      inner = detector.detect(options);
      if (paused) inner.pause();
      if (stopped) inner.stop();
      const r = await inner;
      const files = {};
      for (const name of outputs) files[name] = await output(detector, name);
      const pictures = stills ? await detector.stills(r.shots, { at: stills, width: stillWidth }) : null;
      return { video, ...r, stills: pictures, files };
    } finally {
      detector.close();
    }
  })();
  const run = new Run(result);
  run.stop = () => { stopped = true; inner?.stop(); };
  run.pause = () => { paused = true; inner?.pause(); };
  run.resume = () => { paused = false; inner?.resume(); };
  run.cancel = () => { cancelled = true; if (inner) inner.cancel(); else detector.close(); };
  options.signal?.addEventListener('abort', () => { if (!inner) run.cancel(); });
  return run;
}

function output(detector, name) {
  switch (name) {
    case 'scenesCsv': return detector.scenesCsv();
    case 'statsCsv': return detector.statsCsv();
    case 'shotsJson': return detector.shotsJson();
    case 'shotsHtml': return detector.shotsHtml();
    case 'chapters': return detector.chapters();
    default:
      if (TIMELINES.includes(name)) return detector.timeline(name);
      throw new TypeError(`outputs: no output named ${name}`);
  }
}

// A shot's timecode (HH:MM:SS.mmm) in seconds.
function seconds(timecode) {
  return timecode.split(':').reduce((sum, part) => sum * 60 + Number(part), 0);
}

function abortError() {
  return new DOMException('The run was cancelled.', 'AbortError');
}

// Whether a file is an AVI (RIFF....AVI ), as VideoInput.Open tells containers apart by their first bytes.
async function isAvi(file) {
  const b = new Uint8Array(await file.slice(0, 12).arrayBuffer());
  return String.fromCharCode(...b.subarray(0, 4)) === 'RIFF' && String.fromCharCode(...b.subarray(8, 12)) === 'AVI ';
}

async function fromJpegs(j, times, width, quality) {
  const out = [];
  for (const t of times) {
    let i = 0;
    for (let lo = 0, hi = j.times.length - 1; lo <= hi;) {
      const mid = (lo + hi) >> 1;
      if (j.times[mid] <= t + 1e-6) { i = mid; lo = mid + 1; } else hi = mid - 1;
    }
    let image = null;
    try {
      image = await createImageBitmap(j.file.slice(j.offsets[i], j.offsets[i] + j.sizes[i], 'image/jpeg'));
      out.push(await toJpeg(image, image.width, image.height, width, quality));
    } catch {
      out.push(null);
    } finally {
      image?.close();
    }
  }
  return out;
}

async function fromVideo(file, times, width, quality) {
  const video = document.createElement('video');
  const url = URL.createObjectURL(file);
  video.muted = true;
  video.preload = 'auto';
  video.src = url;
  try {
    const out = [];
    const ready = await settled(video, 'loadedmetadata');
    for (const t of times) {
      if (!ready) { out.push(null); continue; }
      video.currentTime = t;
      out.push(await settled(video, 'seeked') && video.videoWidth
        ? await toJpeg(video, video.videoWidth, video.videoHeight, width, quality) : null);
    }
    return out;
  } finally {
    video.removeAttribute('src');
    video.load();
    URL.revokeObjectURL(url);
  }
}

function toJpeg(source, w, h, width, quality) {
  const canvas = document.createElement('canvas');
  canvas.width = Math.min(width, w);
  canvas.height = Math.max(1, Math.round(canvas.width * h / w));
  canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
  return new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
}

// Whether the element fired the event before an error or SEEK_MS.
function settled(target, event) {
  if (event === 'loadedmetadata' && target.readyState >= HTMLMediaElement.HAVE_METADATA) return Promise.resolve(true);
  return new Promise(resolve => {
    const types = [event, 'error'];
    const end = e => {
      clearTimeout(timer);
      for (const type of types) target.removeEventListener(type, end);
      resolve(e?.type === event);
    };
    const timer = setTimeout(end, SEEK_MS);
    for (const type of types) target.addEventListener(type, end);
  });
}
