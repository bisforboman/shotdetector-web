// One timed WebCodecs run, in a worker of its own: check.js starts a new one for each run, as the page decodes in a
// worker (and on the page's thread, a .NET runtime slowed the runs after it several times over).
//   in:  { name, meta, accel, preload }
//   out: { frames, seconds, layout } (the decode and the copy of each frame out; how the first came), or { error }
import { decodeWithWebCodecs, planes, source } from './decode.js';

onmessage = async ({ data: { name, meta, accel, preload } }) => {
  try {
    const units = await source(name, preload);
    const out = planes(meta);
    const started = performance.now();
    let layout;
    const frames = await decodeWithWebCodecs(meta, accel, units, async frame => {
      const { how } = await out.copy(frame);
      layout ??= how;
    });
    postMessage({ frames, seconds: (performance.now() - started) / 1000, layout });
  } catch (e) {
    postMessage({ error: String(e) });
  }
};
