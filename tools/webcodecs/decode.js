// What check.js and its workers share: the access units as WebCodecsCheck prepare wrote them, VideoDecoder over them,
// and a frame's planes copied out.
export const bytesOf = base64 => Uint8Array.from(atob(base64), c => c.charCodeAt(0));
export const hex = bytes => Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');

// The access units: { data, pts, key, discard } in decode order, from the file WebCodecsCheck prepare wrote (int32
// size, int64 pts, a byte of flags, the data). Read whole before a timed run starts unless it is large, then read as
// it comes.
export async function source(name, preload) {
  const url = `/data/${name}.units`;
  if (preload) {
    const bytes = new Uint8Array(await (await fetch(url)).arrayBuffer());
    return () => parse(bytes);
  }
  return () => stream(url);
}

export function* parse(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  for (let p = 0; p < bytes.length;) {
    const size = view.getInt32(p, true);
    const flags = bytes[p + 12];
    yield { data: bytes.subarray(p + 13, p + 13 + size), pts: Number(view.getBigInt64(p + 4, true)), key: !!(flags & 1), discard: !!(flags & 2) };
    p += 13 + size;
  }
}

export async function* stream(url) {
  const reader = (await fetch(url)).body.getReader();
  let buffer = new Uint8Array(0), position = 0;
  const have = async count => {
    while (buffer.length - position < count) {
      const { done, value } = await reader.read();
      if (done) return false;
      const next = new Uint8Array(buffer.length - position + value.length);
      next.set(buffer.subarray(position));
      next.set(value, buffer.length - position);
      buffer = next;
      position = 0;
    }
    return true;
  };
  while (await have(13)) {
    const view = new DataView(buffer.buffer, buffer.byteOffset + position, 13);
    const size = view.getInt32(0, true);
    const flags = view.getUint8(12);
    const pts = Number(view.getBigInt64(4, true));
    if (!await have(13 + size)) throw new Error(`${url} ends inside an access unit.`);
    yield { data: buffer.subarray(position + 13, position + 13 + size), pts, key: !!(flags & 1), discard: !!(flags & 2) };
    position += 13 + size;
  }
  if (buffer.length > position) throw new Error(`${url} ends inside an access unit's header.`);
}

export const config = (meta, accel) => ({ codec: meta.codec, description: bytesOf(meta.description), hardwareAcceleration: accel });

export async function support(meta) {
  const answers = {};
  for (const accel of ['no-preference', 'prefer-hardware', 'prefer-software']) {
    answers[accel] = (await VideoDecoder.isConfigSupported(config(meta, accel))).supported;
  }
  return { codec: meta.codec, answers };
}

// Decodes every access unit with VideoDecoder and hands each frame in display order (a discarded unit's left out) to
// handle(frame, index), one at a time, closing it after; returns how many there were. A few units at most wait to be
// decoded and a few frames to be handled, as a page decoding a film would keep it.
export async function decodeWithWebCodecs(meta, accel, units, handle) {
  let failure = null, wake = null, pending = 0, index = 0, chain = Promise.resolve();
  const poke = () => {
    const w = wake;
    wake = null;
    w?.();
  };
  const discarded = new Set();
  const decoder = new VideoDecoder({
    output: frame => {
      if (discarded.has(frame.timestamp)) {
        frame.close();
        return;
      }
      const i = index++;
      pending++;
      chain = chain.then(() => failure ? undefined : handle(frame, i)).catch(e => { failure ??= e; })
        .finally(() => { frame.close(); pending--; poke(); });
    },
    error: e => { failure ??= e; poke(); },
  });
  decoder.addEventListener('dequeue', poke);
  decoder.configure(config(meta, accel));
  for await (const unit of units()) {
    if (failure) break;
    if (unit.discard) discarded.add(unit.pts);
    decoder.decode(new EncodedVideoChunk({ type: unit.key ? 'key' : 'delta', timestamp: unit.pts, data: unit.data }));
    // A browser without the dequeue event still gets here again: the timer looks every 10 ms.
    while (!failure && (decoder.decodeQueueSize > 4 || pending > 4)) await new Promise(r => { wake = r; setTimeout(poke, 10); });
  }
  if (!failure) await decoder.flush().catch(e => { failure ??= e; });
  await chain;
  if (decoder.state !== 'closed') decoder.close();
  if (failure) throw failure;
  return index;
}

// Copies a frame's visible Y, U and V planes, packed, into buffer, and says how the frame came (its format and sizes)
// and whether it had them: I420 as it is; another format through copyTo's format option, or NV12 copied as it is and
// its chroma split here. A frame already in RGB (Firefox 136's are BGRX) has no planes to give: its pixels are copied
// as they are, into a buffer of their own, so that a timed run still copies each frame out.
const rgb = ['RGBA', 'RGBX', 'BGRA', 'BGRX'];

export function planes(meta) {
  const w = meta.width, h = meta.height, cw = (w + 1) >> 1, ch = (h + 1) >> 1;
  const sizes = [w * h, cw * ch, cw * ch];
  const i420 = [{ offset: 0, stride: w }, { offset: sizes[0], stride: cw }, { offset: sizes[0] + sizes[1], stride: cw }];
  let nv12 = null, pixels = null;
  const buffer = new Uint8Array(sizes[0] + sizes[1] + sizes[2]);
  async function copy(frame) {
    const r = frame.visibleRect;
    const how = `${frame.format} ${frame.codedWidth}x${frame.codedHeight}, visible ${r.width}x${r.height} at ${r.x},${r.y}`;
    if (r.width !== w || r.height !== h) throw new Error(`The frame is ${how}, not ${w}x${h}.`);
    if (frame.format === 'I420') {
      await frame.copyTo(buffer, { layout: i420 });
      return { how, yuv: true };
    }
    if (rgb.includes(frame.format)) {
      pixels ??= new Uint8Array(4 * w * h);
      await frame.copyTo(pixels, { layout: [{ offset: 0, stride: 4 * w }] });
      return { how, yuv: false };
    }
    try {
      await frame.copyTo(buffer, { layout: i420, format: 'I420' });
      return { how: `${how}, copied as I420`, yuv: true };
    } catch (e) {
      if (frame.format !== 'NV12') throw new Error(`${e} (the frame is ${how})`);
    }
    nv12 ??= new Uint8Array(sizes[0] + 2 * sizes[1]);
    await frame.copyTo(nv12, { layout: [{ offset: 0, stride: w }, { offset: sizes[0], stride: 2 * cw }] });
    buffer.set(nv12.subarray(0, sizes[0]));
    for (let i = 0; i < sizes[1]; i++) {
      buffer[sizes[0] + i] = nv12[sizes[0] + 2 * i];
      buffer[sizes[0] + sizes[1] + i] = nv12[sizes[0] + 2 * i + 1];
    }
    return { how: `${how}, its chroma split here`, yuv: true };
  }
  return { buffer, sizes, copy };
}

