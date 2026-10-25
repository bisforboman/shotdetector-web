// The page's worker: it owns the .NET runtime, the page owns the DOM (docs/decisions/2026-10-24-the-page.md).
//   in:  { type: 'open', file: <a File or Blob> }                  out: { type: 'opened', video: <the YuvVideo> }
//        { type: 'detect', options: { detector, threshold, minSceneLength } }
//                                                                  out: { type: 'progress', done, expected } and
//                                                                       { type: 'shot', shot } (each shot as soon as
//                                                                       it is known) while it runs, then
//                                                                       { type: 'detected', result }
//   on failure:                                                    out: { type: 'error', message }
// Messages are answered in order. A run can't be interrupted (the worker is busy in .NET): to cancel, terminate the
// worker and start another; the shots it posted before stay the page's. The file is read in slices as .NET needs them (blob-read.js), not copied into memory.
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
onmessage = async ({ data }) => {
  try {
    const { Open, Detect, ScenesCsv, StatsCsv } = await api;
    switch (data.type) {
      case 'open':
        postMessage({ type: 'opened', video: JSON.parse(Open(data.file)) });
        break;
      case 'detect': {
        // The CSVs come as strings of their own, not inside Detect's JSON (README "Large files").
        const result = JSON.parse(Detect(JSON.stringify(data.options ?? {})));
        postMessage({ type: 'detected', result: { ...result, scenesCsv: ScenesCsv(), statsCsv: StatsCsv() } });
        break;
      }
      default:
        throw new Error(`Unknown message type: ${data.type}`);
    }
  } catch (e) {
    postMessage({ type: 'error', message: e.message });
  }
};
