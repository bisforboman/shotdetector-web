// The page's worker: it owns the .NET runtime, the page owns the DOM (docs/decisions/2026-10-24-the-page.md). It calls
// any of Api's exports by name (decision 2026-10-25-one-page.md), so a new export needs no change here:
//   in:  { type: 'call', id, name, args }        out: { type: 'result', id, value } (what the export returned)
//                                                     { type: 'error', id, message } (what it threw)
//   while an export runs, its imports post:          { type: 'progress', done, expected } and
//                                                     { type: 'shot', shot } (Detect's, each shot as soon as it is known)
// Calls are answered in order, one at a time. One can't be interrupted (the worker is busy in .NET): to cancel, the
// page terminates the worker and starts another. The file is read in slices as .NET needs them (blob-read.js), not
// copied into memory.
import { dotnet } from './_framework/dotnet.js';
import { read } from './blob-read.js';

async function start() {
  const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
  setModuleImports('host', {
    progress: (done, expected) => postMessage({ type: 'progress', done, expected }),
    shot: json => postMessage({ type: 'shot', shot: JSON.parse(json) }),
    read,
  });
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  await runMain();
  return exports.ShotDetectorWeb.App.Api;
}

// Listening before the runtime is up, so that no message sent meanwhile is lost.
const api = start();
onmessage = async ({ data: { type, id, name, args = [] } }) => {
  try {
    if (type !== 'call') throw new Error(`Unknown message type: ${type}`);
    const exported = (await api)[name];
    if (typeof exported !== 'function') throw new Error(`No export named ${name}.`);
    postMessage({ type: 'result', id, value: exported(...args) });
  } catch (e) {
    postMessage({ type: 'error', id, message: e.message });
  }
};
