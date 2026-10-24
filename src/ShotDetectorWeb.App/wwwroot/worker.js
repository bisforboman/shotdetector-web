// The page's worker: it owns the .NET runtime, the page owns the DOM (docs/decisions/2026-10-24-the-page.md).
//   in:  { type: 'open', file: <ArrayBuffer or Uint8Array> }       out: { type: 'opened', video: <the YuvVideo> }
//        { type: 'detect', options: { detector, threshold, minSceneLength } }
//                                                                  out: { type: 'progress', done, expected } while it
//                                                                       runs, then { type: 'detected', result }
//   on failure:                                                    out: { type: 'error', message }
// Messages are answered in order. A run can't be interrupted (the worker is busy in .NET): to cancel, terminate the
// worker and start another.
import { dotnet } from './_framework/dotnet.js';

async function start() {
  const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
  setModuleImports('host', {
    progress: (done, expected) => postMessage({ type: 'progress', done, expected }),
  });
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  await runMain();
  return exports.ShotDetectorWeb.App.Api;
}

// Listening before the runtime is up, so that no message sent meanwhile is lost.
const api = start();
onmessage = async ({ data }) => {
  try {
    const { Open, Detect } = await api;
    switch (data.type) {
      case 'open':
        postMessage({ type: 'opened', video: JSON.parse(Open(new Uint8Array(data.file))) });
        break;
      case 'detect':
        postMessage({ type: 'detected', result: JSON.parse(Detect(JSON.stringify(data.options ?? {}))) });
        break;
      default:
        throw new Error(`Unknown message type: ${data.type}`);
    }
  } catch (e) {
    postMessage({ type: 'error', message: e.message });
  }
};
