// Bitrate and keyframes (backlog "Bitrate and keyframes"): the encoder's view of the cuts next to ShotDetector's. A lane
// of the timeline with the video's bitrate per second as a graph and a tick at each keyframe, and a card with the
// numbers and how many cuts fall on a keyframe. It needs no decoding: Api's Packets (each video packet's pts, size and
// flags, as ffprobe -show_packets lists them, checked against it: README, Checks) comes right after the file is opened,
// before the run. The packets are the file's; the bitrate, the spacing and the cuts on keyframes are the page's own
// reading of them, and the card says so. Packets an edit list discards (decoded, not shown) are left out of both.
import { card, on, call } from '../app.js';
import { lane, timecode, x } from '../timeline.js';

const SVG = 'http://www.w3.org/2000/svg';

let packets = null; // the open file's packets, read for the page (read() below), once Packets() has come
let opened = null;  // the YuvVideo they are for

const section = card('Bitrate and keyframes');
section.id = 'bitrate';
section.hidden = true;
const facts = document.createElement('div'), cuts = document.createElement('p'), note = document.createElement('p');
note.className = 'note';
note.append('From the MP4\'s video packets, without decoding: their sizes and keyframe flags are the file\'s, checked ' +
  'against ffprobe -show_packets. ', own('Underlined dotted: the page\'s own reading.', ''));
section.append(facts, cuts, note);

// One SVG over the video's span, in x()'s percentages: drawn again only for a new duration, not for a new width.
const bitrateLane = lane('Bitrate', track => {
  track.replaceChildren(...(packets ? [svg(packets)] : []));
});
bitrateLane.track.classList.add('bitrate');
bitrateLane.label.classList.add('bitrate');
bitrateLane.label.title = 'The video\'s bitrate per second (the page\'s reading of the packets\' sizes), and a tick at ' +
  'each keyframe';
bitrateLane.show(false);

// Packets()'s columns as the page uses them: the shown packets' times in seconds from the first frame (pts minus the
// smallest shown pts: the cuts lane's times, a frame number over the rate), the bits in each second of that, and the
// keyframes' times and frame numbers (the frames in display order: a keyframe's rank among the shown pts).
function read({ timeBase: { num, den }, pts, sizes, keyframes, discarded }, { frameRate }) {
  const gone = new Set(discarded), shown = [];
  for (let i = 0; i < pts.length; i++) if (!gone.has(i)) shown.push(i);
  const sorted = shown.map(i => pts[i]).sort((a, b) => a - b);
  const first = sorted[0] ?? 0, seconds = p => (p - first) * num / den;
  const end = sorted.length === 0 ? 0 : seconds(sorted.at(-1)) + frameRate.den / frameRate.num;
  const bits = new Float64Array(Math.ceil(end));
  let bytes = 0;
  for (const i of shown) {
    bits[Math.min(Math.floor(seconds(pts[i])), bits.length - 1)] += sizes[i] * 8;
    bytes += sizes[i];
  }
  // Bits per second: the last second's bits over the part of it the video lasts.
  const rate = Array.from(bits, (b, s) => b / (Math.min(s + 1, end) - s));
  const frameOf = new Map(sorted.map((p, f) => [p, f]));
  const keys = keyframes.filter(i => !gone.has(i)).map(i => ({ at: seconds(pts[i]), frame: frameOf.get(pts[i]) }));
  return { count: pts.length, shown: shown.length, discarded: discarded.length, bytes, end, rate, keys };
}

// The bitrate as a filled step graph, each second's rate its height (the most in one second the lane's), and the
// keyframes as ticks over it: the viewBox's x is seconds, its y the lane's height from 0 (top) to 1.
function svg({ end, rate, keys }) {
  const s = document.createElementNS(SVG, 'svg');
  s.setAttribute('viewBox', `0 0 ${end} 1`);
  s.setAttribute('preserveAspectRatio', 'none');
  s.setAttribute('aria-hidden', 'true');
  s.style.left = x(0);
  s.style.width = x(end);
  const most = Math.max(...rate, 1), y = r => (1 - 0.9 * r / most).toFixed(4);
  let d = 'M0 1';
  rate.forEach((r, i) => { d += `V${y(r)}H${Math.min(i + 1, end)}`; });
  const area = document.createElementNS(SVG, 'path');
  area.setAttribute('class', 'rate');
  area.setAttribute('d', `${d}V1Z`);
  const ticks = document.createElementNS(SVG, 'path');
  ticks.setAttribute('class', 'keyframes');
  ticks.setAttribute('d', keys.map(k => `M${k.at.toFixed(4)} 0V1`).join(''));
  s.append(area, ticks);
  return s;
}

function show(p) {
  const average = p.end > 0 ? p.bytes * 8 / p.end : 0;
  const most = p.rate.reduce((m, r, i) => (r > p.rate[m] ? i : m), 0);
  const spacing = p.keys.length > 1 ? (p.keys.at(-1).at - p.keys[0].at) / (p.keys.length - 1) : null;
  facts.replaceChildren(
    paragraph(`${p.count.toLocaleString('en')} video packets, ${megabytes(p.bytes)}, ` +
      `${p.keys.length.toLocaleString('en')} ${p.keys.length === 1 ? 'keyframe' : 'keyframes'}` +
      `${p.discarded > 0 ? `; ${p.discarded} discarded by the edit list (decoded, not shown), left out` : ''}.`),
    paragraph(
      own(`${mbits(average)} on average`, 'The page\'s own: the shown packets\' bits over the video\'s length'), ', ',
      own(`${mbits(p.rate[most] ?? 0)} at most in one second`, 'The page\'s own: the most bits in one second of the video'),
      ` (from ${timecode(most)}).`),
    ...(spacing === null ? [] : [paragraph(own(`A keyframe every ${spacing.toFixed(1)} s on average.`,
      'The page\'s own: the time from the first keyframe to the last over the keyframes between them'))]));
  section.hidden = false;
}

// How many cuts fall on a keyframe: a cut does when its shot's first frame is a keyframe (the frames in display order;
// a shot's startFrame counts them from 1, as scenedetect's CSV does).
function showCuts(shots) {
  if (!packets) return;
  const keyFrames = new Set(packets.keys.map(k => k.frame));
  const starts = shots.filter(s => s.shot > 1).map(s => s.startFrame - 1);
  const at = new Set(starts), on = starts.filter(f => keyFrames.has(f)).length;
  const elsewhere = packets.keys.filter(k => k.frame !== 0 && !at.has(k.frame)).length;
  cuts.replaceChildren(own(
    `${on} of ${starts.length} ${starts.length === 1 ? 'cut falls' : 'cuts fall'} on a keyframe; ` +
    `${elsewhere} ${elsewhere === 1 ? 'keyframe is' : 'keyframes are'} at no cut (the first frame's aside).`,
    'The page\'s reading: a cut falls on a keyframe when its shot\'s first frame is one'));
}

function clear() {
  packets = null;
  opened = null;
  section.hidden = true;
  facts.replaceChildren();
  cuts.replaceChildren();
  bitrateLane.show(false);
  bitrateLane.draw();
}

on('chose', clear);
on('opened', async ({ video }) => {
  clear();
  opened = video;
  let json;
  try {
    json = await call('Packets');
  } catch (e) {
    if (e.name !== 'AbortError') facts.replaceChildren(paragraph(`No packets: ${e.message}`));
    section.hidden = e.name === 'AbortError';
    return;
  }
  if (opened !== video) return;
  packets = read(JSON.parse(json), video);
  show(packets);
  bitrateLane.show(true);
  bitrateLane.draw();
});
on('run', () => cuts.replaceChildren());
on('done', ({ video, result }) => { if (video === opened) showCuts(result.shots); });

function own(text, title) {
  const span = document.createElement('span');
  span.className = 'own';
  span.textContent = text;
  if (title) span.title = title;
  return span;
}

function paragraph(...parts) {
  const p = document.createElement('p');
  p.append(...parts);
  return p;
}

const megabytes = bytes => `${(bytes / 1e6).toFixed(bytes < 1e7 ? 2 : 1)} MB`;
const mbits = bitsPerSecond => `${(bitsPerSecond / 1e6).toFixed(2)} Mbit/s`;
