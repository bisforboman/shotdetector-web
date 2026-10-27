// WebCodecs against ManagedCodecs (README, "WebCodecs"), in whatever browser opens check.html from serve.mjs:
//   ?compare=all (or sample names, comma-separated): decodes each sample's access units (ManagedCodecs' MP4 demuxer's,
//     from tools/WebCodecsCheck) with VideoDecoder, once per ?accel= (default prefer-software,prefer-hardware) that
//     isConfigSupported accepts, copies each frame's Y, U and V planes out (VideoFrame.copyTo, I420, packed), and
//     compares their SHA-256 with ManagedCodecs' frame by frame; the first frame that differs goes to serve.mjs, which
//     says where and by how much.
//   ?time=names: times the decode plus the copy, ?repeat= times (default 3), against ManagedCodecs' decoder on wasm fed
//     the same access units (tools/WebCodecsWasm, when serve.mjs has --wasm); the runs alternate, each in a new worker.
// Each pass's result is posted to serve.mjs; the page's title becomes "done" at the end.
import { decodeWithWebCodecs, hex, planes, source, support } from './decode.js';

const params = new URLSearchParams(location.search);
const plan = await (await fetch('/plan')).json();
const names = key => {
  const value = params.get(key);
  return !value ? [] : value === 'all' ? plan.samples : value.split(',');
};
const compare = names('compare');
const time = names('time');
const repeat = Number(params.get('repeat') ?? 3);
const accels = (params.get('accel') ?? 'prefer-software,prefer-hardware').split(',');
const browser = await browserName();
const logElement = document.getElementById('log');

function log(text) {
  logElement.textContent += text + '\n';
  fetch('/log', { method: 'POST', body: `${browser}: ${text}` });
}

async function browserName() {
  const ua = navigator.userAgent;
  let m = ua.match(/Firefox\/([\d.]+)/);
  if (m) return `Firefox ${m[1]}`;
  if (navigator.userAgentData) {
    const { fullVersionList } = await navigator.userAgentData.getHighEntropyValues(['fullVersionList']);
    const brand = fullVersionList.find(b => /Chrom/.test(b.brand));
    if (brand) return `${brand.brand} ${brand.version}`;
  }
  m = ua.match(/Version\/([\d.]+).*Safari/);
  return m ? `Safari ${m[1]}` : ua;
}

async function post(path, query, body) {
  const response = await fetch(`${path}?${new URLSearchParams({ browser, ...query })}`, { method: 'POST', body });
  if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
  return response;
}

async function hash(bytes) {
  return hex(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)).subarray(0, 16));
}

// A frame that came out in RGB: nothing to compare, and no need to decode the rest.
class NotYuv extends Error {}

async function comparePass(name, meta, accel, supported) {
  const result = { kind: 'compare', browser, sample: name, accel, support: supported, expected: meta.frames, frames: 0 };
  if (!supported.answers[accel]) return { ...result, unsupported: true };
  const expected = (await (await fetch(`/data/${name}.hashes`)).text()).trim().split('\n').map(line => line.split(' '));
  const out = planes(meta);
  const differing = { Y: 0, U: 0, V: 0 };
  let firstFrame = null, ptsDiffer = 0;
  const started = performance.now();
  try {
    result.frames = await decodeWithWebCodecs(meta, accel, await source(name, false), async (frame, i) => {
      const { how, yuv } = await out.copy(frame);
      if (i === 0) {
        result.layout = how;
        result.colorSpace = frame.colorSpace.toJSON();
      }
      if (!yuv) throw new NotYuv();
      const want = expected[i];
      if (!want) return;
      if (String(frame.timestamp) !== want[0]) {
        ptsDiffer++;
        result.firstPts ??= { frame: i, got: frame.timestamp, want: Number(want[0]) };
      }
      let offset = 0, differs = false;
      for (const [p, plane] of ['Y', 'U', 'V'].entries()) {
        if (await hash(out.buffer.subarray(offset, offset + out.sizes[p])) !== want[p + 1]) {
          differing[plane]++;
          differs = true;
        }
        offset += out.sizes[p];
      }
      if (differs && !firstFrame) firstFrame = { frame: i, planes: out.buffer.slice() };
    });
  } catch (e) {
    if (e instanceof NotYuv) result.notYuv = true;
    else result.error = String(e);
  }
  result.seconds = (performance.now() - started) / 1000;
  result.ptsDiffer = ptsDiffer;
  if (differing.Y || differing.U || differing.V) result.differing = differing;
  if (firstFrame) {
    const response = await post('/frame', { pass: `compare-${name}-${accel}`, sample: name, frame: firstFrame.frame }, firstFrame.planes);
    result.firstDiff = { frame: firstFrame.frame, ...await response.json() };
  }
  return result;
}

// A run in a new worker, started before the clock: it times itself from the first access unit.
function timedRun(script, message) {
  const worker = new Worker(script, { type: 'module' });
  return new Promise(resolve => {
    worker.onmessage = ({ data }) => resolve(data);
    worker.onerror = e => resolve({ error: e.message ?? 'the worker failed' });
    worker.postMessage(message);
  }).finally(() => worker.terminate());
}

async function timePass(name, meta) {
  const preload = meta.bytes < 512 * 1024 * 1024;
  const supported = await support(meta);
  const runs = accels.filter(a => supported.answers[a]).map(accel => ({ decoder: `WebCodecs, ${accel}`, script: 'webcodecs-worker.js', accel }));
  if (plan.wasm) runs.push({ decoder: 'ManagedCodecs, wasm', script: 'managedcodecs-worker.js' });
  const results = runs.map(d => ({ kind: 'time', browser, sample: name, decoder: d.decoder, frames: 0, seconds: [], preload, support: supported }));
  for (let r = 0; r < repeat; r++) {
    for (const [i, d] of runs.entries()) {
      const { frames, seconds, layout, error } = await timedRun(d.script, { name, meta, preload, accel: d.accel });
      if (error) {
        results[i].error = error;
        log(`${name}: ${d.decoder}, run ${r + 1}: ${error}`);
        continue;
      }
      results[i].frames = frames;
      results[i].layout = layout;
      results[i].seconds.push(seconds);
      log(`${name}: ${d.decoder}, run ${r + 1}: ${frames} frames in ${seconds.toFixed(2)} s`);
    }
  }
  for (const result of results) await post('/result', { pass: `time-${name}-${result.decoder}` }, JSON.stringify(result));
}

log(`compare ${compare.join(', ') || 'nothing'}; time ${time.join(', ') || 'nothing'}`);
for (const name of compare) {
  const meta = await (await fetch(`/data/${name}.json`)).json();
  const supported = await support(meta);
  for (const accel of accels) {
    const result = await comparePass(name, meta, accel, supported);
    await post('/result', { pass: `compare-${name}-${accel}` }, JSON.stringify(result));
    const verdict = result.unsupported ? 'not supported' : result.notYuv ? 'frames in RGB, no Y, U and V planes' : result.error ? `error: ${result.error}`
      : result.differing || result.ptsDiffer || result.frames !== result.expected ? `DIFFERS ${JSON.stringify(result.differing ?? {})}` : 'every frame identical';
    log(`${name}, ${accel}: ${result.frames} of ${result.expected} frames, ${verdict}` +
      (result.seconds ? ` (${result.seconds.toFixed(1)} s; ${result.layout})` : ''));
  }
}
for (const name of time) await timePass(name, await (await fetch(`/data/${name}.json`)).json());
await post('/done', {});
log('done');
document.title = 'done';
