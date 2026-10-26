// One timed ManagedCodecs run on wasm (tools/WebCodecsWasm, served by serve.mjs --wasm), in a worker of its own as
// webcodecs-worker.js's are, fed the same access units. dotnet.js is imported statically: a dynamic import() of it
// here crashed Chromium 153's renderer as the runtime started.
//   in:  { name, meta, preload }    out: { frames, seconds } (the decode), or { error }
import { dotnet } from '/wasm/_framework/dotnet.js';
import { bytesOf, source } from './decode.js';

onmessage = async ({ data: { name, meta, preload } }) => {
  try {
    const units = await source(name, preload);
    const { getAssemblyExports, getConfig, runMain } = await dotnet.create();
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    await runMain();
    const decoding = exports.ShotDetectorWeb.WebCodecsWasm.Decoding;
    const started = performance.now();
    decoding.Start(bytesOf(meta.description));
    let frames = 0;
    for await (const unit of units()) frames += decoding.Decode(unit.data, unit.pts, unit.key, unit.discard);
    frames += decoding.Flush();
    postMessage({ frames, seconds: (performance.now() - started) / 1000 });
  } catch (e) {
    postMessage({ error: String(e) });
  }
};
