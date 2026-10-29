// The page's worker: it owns the .NET runtime, the page owns the DOM (docs/decisions/2026-10-24-the-page.md). It calls
// any of Api's exports by name (decision 2026-10-25-one-page.md), so a new export needs no change here:
//   in:  { type: 'call', id, name, args }        out: { type: 'result', id, value } (what the export returned, or the
//                                                       value its promise resolved to)
//                                                     { type: 'error', id, message } (what it threw)
//   while an export runs, its imports post:          { type: 'progress', done, expected } and
//                                                     { type: 'shot', shot } (Detect's, each shot as soon as it is known)
// Calls are answered in order, one at a time: a call starts once the one before it has its answer. While Detect runs
// it pauses every few frames for the worker's next task (pause.js), and two messages are handled at once, between
// those pauses, without waiting their turn:
//   in:  { type: 'stop' }    Detect's frames end at its next pause, as if the file ended there: it answers with the
//                            result for the frames read (Api's Stop; decision "Cancelling a run").
//        { type: 'cancel' }  the running call is abandoned (Api's Cancel): out: { type: 'cancelled', id } instead of
//                            its answer (an OperationCanceledException is not an error).
//        { type: 'pause' }   the run waits at its next pause until 'resume' (pause.js's hold and release): nothing is
//        { type: 'resume' }  cancelled or ended. A stop or a cancel while paused ends the run there.
// A worker that doesn't answer at all (a long decode between two pauses, a crash) the page terminates. The file is read
// in slices as .NET needs them (blob-read.js), not copied into memory.
import { dotnet } from './_framework/dotnet.js';
import { read } from './blob-read.js';
import { hold, pause, release } from './pause.js';

async function start() {
  const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
  setModuleImports('host', {
    progress: (done, expected) => postMessage({ type: 'progress', done, expected }),
    shot: json => postMessage({ type: 'shot', shot: JSON.parse(json) }),
    read,
    pause,
  });
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  await runMain();
  return exports.ShotDetectorWeb.App.Api;
}

// Listening before the runtime is up, so that no message sent meanwhile is lost.
const api = start();
let turn = Promise.resolve(); // the last call's answer: the next call starts after it
let running = null;           // the call running, { id, cancelled }

const now = {
  async stop() {
    (await api).Stop();
    release();
  },
  async cancel() {
    if (running) running.cancelled = true;
    (await api).Cancel();
    release();
  },
  pause() { hold(); },
  resume() { release(); },
};

async function answer({ type, id, name, args = [] }) {
  const call = running = { id, cancelled: false };
  try {
    if (type !== 'call') throw new Error(`Unknown message type: ${type}`);
    const exported = (await api)[name];
    if (typeof exported !== 'function') throw new Error(`No export named ${name}.`);
    postMessage({ type: 'result', id, value: await exported(...args) });
  } catch (e) {
    postMessage(call.cancelled ? { type: 'cancelled', id } : { type: 'error', id, message: e.message });
  } finally {
    running = null;
  }
}

onmessage = ({ data }) => {
  if (data.type in now) now[data.type]();
  else turn = turn.then(() => answer(data));
};
