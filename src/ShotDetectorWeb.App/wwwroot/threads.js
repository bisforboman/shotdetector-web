// A one-worker run with the decoder on several threads (decision 2026-10-30-threads.md): the threaded bundle
// (threads/_framework/, published with WasmEnableThreads) on the page's own thread, which is the only place .NET's
// threaded runtime starts (not in a Web Worker), and only where the page is cross-origin isolated (sw.js). .NET runs
// the exports on a thread of its own and the run on its thread pool (Host.cs), so the page stays responsive. A runner
// looks like worker.js to app.js: postMessage the same messages, onmessage the same answers and progress, shots and
// 'held'. Only Open, Detect, Resume, the last run's result (RESULTS), Stop and Cancel are called here (the threaded exports,
// Api.Threads.cs); everything else stays with the page's worker. Unlike a worker it can't be terminated: a runner that
// is ended is abandoned (its run cancelled) and, the runtime being one per page, no threaded run starts again before a
// reload; one whose runtime ends calls its onerror, as a worker that dies does.
import { hold, pause, release, whenHeld } from './pause.js';
import { append, blob, parts } from './blob-write.js';

// The exports' names in the threaded bundle: tasks, as .NET on the main thread calls nothing synchronously
// (Api.Threads.cs). The last run's result: RESULTS, which app.js asks of the runner that ran it.
const THREADED = {
  Open: 'OpenThreaded', Detect: 'Detect', ScenesCsv: 'ScenesCsvThreaded', StatsCsv: 'StatsCsvThreaded',
  ShotsJson: 'ShotsJsonThreaded', ShotsHtml: 'ShotsHtmlThreaded', Picture: 'PictureThreaded',
  FrameTypes: 'FrameTypesThreaded', TimelineFile: 'TimelineFileThreaded',
  YouTubeChapters: 'YouTubeChaptersThreaded', Resume: 'Resume', Kept: 'KeptThreaded',
};
export const RESULTS = new Set(['ScenesCsv', 'StatsCsv', 'ShotsJson', 'ShotsHtml', 'Picture', 'FrameTypes', 'TimelineFile',
  'YouTubeChapters', 'Kept']);

let api = null;     // the runtime's Api, once started (a promise)
let current = null; // the runner whose run the runtime's imports report to
let broken = false; // a runner was abandoned (or the runtime failed): threads are off until a reload

// Whether a run can go on threads here: the page cross-origin isolated (SharedArrayBuffer, which the threaded runtime
// needs), and no runner given up on.
export const threadsUsable = () => globalThis.crossOriginIsolated === true && !broken;

function start() {
  api ??= (async () => {
    const { dotnet } = await import('./threads/_framework/dotnet.js');
    // A runtime that ends (an abort, an exit) takes its runs with it: the runner fails, as a worker that dies does.
    const died = message => {
      broken = true;
      current?.onerror?.({ message: `The threaded runtime stopped: ${message}`, preventDefault() {} });
    };
    const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
      .withModuleConfig({ onAbort: reason => died(String(reason?.message ?? reason)), onExit: code => died(`exit ${code}`) })
      .create();
    setModuleImports('host', {
      progress: (done, expected) => current?.tell({ type: 'progress', done, expected }),
      shot: json => current?.tell({ type: 'shot', shot: JSON.parse(json) }),
      pause,
      // BlobStream's slices: read asynchronously (the main thread has no FileReaderSync) while .NET's pool waits.
      readAsync: async (file, position, length) => new Uint8Array(await file.slice(position, position + length).arrayBuffer()),
      copy: (bytes, view) => {
        view.set(bytes);
        return bytes.length;
      },
      parts,
      append,
      blob,
    });
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    await runMain();
    return exports.ShotDetectorWeb.App.Api;
  })();
  api.catch(() => { broken = true; });
  return api;
}

whenHeld(frames => current?.tell({ type: 'held', frames }));

// Starts the threaded runtime ahead of the run that needs it, where it can start (threadsUsable): app.js calls it when a
// file is picked or the demo fetched, so that its 31 files (5.6 MB gzipped) load and the runtime starts while the
// visitor sets the options or the clip downloads, not after Detect (backlog "Performance pass on the page": the first
// progress came 1.1 to 1.6 s later on threads than on the page's worker). A runtime that fails turns threads off, as
// in a run.
export function warm() {
  if (threadsUsable()) start().catch(() => {});
}

export class ThreadedRunner {
  // threads: the decoder's, VideoInput.DecoderThreads.
  constructor(threads) {
    this.threads = threads;
    this.onmessage = null;
    this.onerror = null;
    this.ended = false;
    this.turn = Promise.resolve();
    this.running = null;
    current = this;
    start();
  }

  tell(data) {
    if (!this.ended) this.onmessage?.({ data });
  }

  postMessage(data) {
    if (this.ended) return;
    switch (data.type) {
      case 'stop': this.now(api => api.StopThreaded()); break;
      case 'cancel':
        if (this.running) this.running.cancelled = true;
        this.now(api => api.CancelThreaded());
        break;
      case 'pause': hold(); break;
      case 'resume': release(); break;
      default: this.turn = this.turn.then(() => this.answer(data));
    }
  }

  // A stop or a cancel, told to .NET before the held run (if paused) is let go to end at its pause, as worker.js does.
  async now(f) {
    try { await f(await start()); } finally { release(); }
  }

  async answer({ type, id, name, args = [] }) {
    const call = this.running = { id, cancelled: false };
    try {
      if (type !== 'call' || !(name in THREADED)) throw new Error(`Not a threaded call: ${type} ${name}`);
      const exports = await start();
      const value = name === 'Open' ? await exports[THREADED[name]](...args, this.threads) : await exports[THREADED[name]](...args);
      this.tell({ type: 'result', id, value });
    } catch (e) {
      this.tell(call.cancelled ? { type: 'cancelled', id } : { type: 'error', id, message: e.message });
    } finally {
      this.running = null;
    }
  }

  // Abandoned: its run cancelled at its next pause, nothing more told; threads stay off until a reload.
  terminate() {
    if (this.ended) return;
    this.ended = true;
    broken = true;
    if (current === this) current = null;
    start().then(api => api.CancelThreaded()).catch(() => {}).finally(release);
  }
}
