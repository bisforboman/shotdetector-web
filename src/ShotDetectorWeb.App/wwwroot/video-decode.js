// The host's decoder imports that .NET's JsDecoder (src/ShotDetectorWeb.App/JsDecoder.cs) uses for WebM's VP9 and AV1
// (decision 2026-11-02-webm-browser-decoder.md): WebCodecs' VideoDecoder in this worker, software first (Chromium's,
// whose frames were FFmpeg's in the measurement), each frame copied out packed in the video's format (copyTo, in the
// order the decoder gives them) and taken by .NET one at a time. One decoder at a time; worker.js passes these to the
// runtime.
let decoder = null;
let format = '';     // the format .NET takes: I420, I422 or I420P10
let ready = [];      // frames copied out, in order, not yet taken
let copying = Promise.resolve(); // the copies, one after another, so they stay in order
let failed = null;   // the decoder's error, or the frames' format refused
let wake = null;     // resolves a waiting decoderNext
let flushed = false;

const SOFTWARE = ['prefer-software', 'no-preference'];
// Packets the decoder may hold before .NET waits for a frame.
const QUEUE = 4;

function poke() {
  const w = wake;
  wake = null;
  w?.();
}

// A browser VideoFrame's planes, packed (Y, then U, then V) as .NET's frame is laid out: as the browser gives them, or
// NV12's interleaved chroma split (a hardware decoder's). Anything else (BGRX: Firefox's) has no Y, U and V to give.
async function packed(frame) {
  const f = frame.format;
  if (f === format || (format === 'I420' && f === 'I420A')) {
    const all = new Uint8Array(frame.allocationSize());
    await frame.copyTo(all);
    return all;
  }
  if (format === 'I420' && f === 'NV12') {
    const w = frame.visibleRect.width, h = frame.visibleRect.height, cw = (w + 1) >> 1, ch = (h + 1) >> 1;
    const nv12 = new Uint8Array(frame.allocationSize());
    await frame.copyTo(nv12);
    const out = new Uint8Array(w * h + 2 * cw * ch);
    out.set(nv12.subarray(0, w * h));
    const uv = nv12.subarray(w * h);
    for (let i = 0; i < cw * ch; i++) {
      out[w * h + i] = uv[2 * i];
      out[w * h + cw * ch + i] = uv[2 * i + 1];
    }
    return out;
  }
  throw new Error(`the browser gives its frames as ${f}, not as Y, U and V planes (${format}), so they can't go ` +
    'through the page\'s own conversion.');
}

export async function decoderOpen(codec, width, height, wanted) {
  decoderClose();
  if (typeof VideoDecoder === 'undefined')
    return 'it has no WebCodecs VideoDecoder.';
  let config = null;
  for (const hardwareAcceleration of SOFTWARE) {
    const c = { codec, codedWidth: width, codedHeight: height, hardwareAcceleration };
    try {
      if ((await VideoDecoder.isConfigSupported(c)).supported) { config = c; break; }
    } catch { /* an invalid codec string: not supported */ }
  }
  if (!config) return `its VideoDecoder doesn't take ${codec}.`;
  format = wanted;
  ready = [];
  failed = null;
  flushed = false;
  copying = Promise.resolve();
  decoder = new VideoDecoder({
    output: frame => {
      copying = copying.then(() => failed ? null : packed(frame)).then(
        bytes => { if (bytes) ready.push(bytes); },
        e => { failed ??= e; }).finally(() => { frame.close(); poke(); });
    },
    error: e => { failed ??= e; poke(); },
  });
  decoder.addEventListener('dequeue', poke);
  decoder.configure(config);
  return '';
}

export function decoderDecode(view, pts, keyframe) {
  // view.slice(): a copy (the .NET Span is valid only during the call).
  decoder.decode(new EncodedVideoChunk({ type: keyframe ? 'key' : 'delta', timestamp: pts, data: view.slice() }));
}

// 1: a frame is ready; 0: send the next packet; -1: flushed and every frame taken.
export async function decoderNext(end) {
  if (end && !flushed) {
    flushed = true;
    const flushing = decoder.flush().catch(e => { failed ??= e; });
    await flushing;
    await copying;
  }
  for (;;) {
    if (ready.length > 0) return 1;
    if (failed) throw failed instanceof Error ? failed : new Error(String(failed));
    if (end) {
      await copying;
      if (ready.length > 0) continue;
      if (failed) continue;
      return -1;
    }
    if (decoder.decodeQueueSize < QUEUE) return 0;
    await new Promise(resolve => { wake = resolve; });
  }
}

export function decoderTake(view) {
  const bytes = ready.shift();
  view.set(bytes.subarray(0, view.length));
}

export function decoderClose() {
  if (decoder && decoder.state !== 'closed') decoder.close();
  decoder = null;
  ready = [];
  poke();
}
