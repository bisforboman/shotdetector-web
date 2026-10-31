// Frame types (backlog "Frame types (I, P, B) on the strip"): the encoder's structure, where its GOPs start and where
// the B-frames go, next to the bitrate and the keyframes. Every run keeps each frame's type as ManagedCodecs' decoder
// says it (FFmpeg's pict_type; the core's PictureMeter, a split run each range in its worker), and Api's FrameTypes
// gives them after the run, one character a frame in display order. A lane of the timeline, right under the bitrate's:
// a bar a frame, I tall, P half as tall, B shorter, each in its colour; where frames are narrower than a pixel, a column
// shows each type it holds. A card with the counts. The types are the core's, checked frame by frame against
// ffprobe -show_frames (README, Checks): the card's mark says "core"; the longest run of B-frames is the page's own
// reading of them, and says so in its tooltip.
import { call, card, frameSeconds, on } from '../app.js';
import { lane, x } from '../timeline.js';

// Each type's bar: its share of the lane's height and its colour (a custom property of the track). The rarer types
// (S, SI, SP, BI, none: not from the H.264 and MJPEG the core takes) as P.
const BARS = { I: [1, '--type-i'], P: [0.6, '--type-p'], B: [0.3, '--type-b'] };
const bar = t => BARS[t] ?? BARS.P;

let types = null;  // the last run's FrameTypes(), with the video it is for
let opened = null; // the opened file's YuvVideo
let asked = 0;     // how many runs began: an answer after a new run or file draws nothing

const section = card('Frame types', {
  mark: 'core',
  markTitle: 'The core\'s: each frame\'s type as ManagedCodecs\' decoder gives it (FFmpeg\'s pict_type), checked frame ' +
    'by frame against ffprobe -show_frames, exactly. An I frame is not always a keyframe: the bitrate\'s keyframes are ' +
    'the file\'s flags',
});
section.id = 'frametypes';
section.hidden = true;

const canvas = document.createElement('canvas');
canvas.setAttribute('aria-hidden', 'true');

const typesLane = lane('Frame types', track => {
  if (!types) return track.replaceChildren();
  draw(track);
  track.replaceChildren(canvas);
});
typesLane.track.classList.add('frametypes');
typesLane.label.classList.add('frametypes');
typesLane.label.title = 'Each frame\'s type, as the decoder gives it: I tall, P shorter, B shortest (where frames are ' +
  'narrower than a pixel, each type a column holds)';
typesLane.show(false);

function draw(track) {
  const { text, video } = types;
  const width = track.clientWidth, height = track.clientHeight, ratio = devicePixelRatio || 1;
  canvas.width = Math.round(width * ratio);
  canvas.height = Math.round(height * ratio);
  const w = canvas.width, h = canvas.height;
  // Where frame f starts, in the canvas's pixels: x() is a percentage of the track's width, linear from 0; perFrame the
  // frames' mean width, which decides between a bar a frame and columns.
  const start = f => parseFloat(x(frameSeconds(video, f))) / 100 * w;
  const perFrame = text.length > 0 ? start(text.length) / text.length : 0;
  const style = getComputedStyle(track), ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, w, h);
  const fill = (t, from, wide) => {
    const [share, colour] = bar(t);
    ctx.fillStyle = style.getPropertyValue(colour);
    ctx.fillRect(from, Math.round(h * (1 - share)), wide, h);
  };

  if (perFrame >= 1) {
    // A bar a frame, a pixel apart once they are wide enough to tell.
    const gap = perFrame >= 3 * ratio ? Math.round(ratio) : 0;
    for (let f = 0; f < text.length; f++) {
      const from = Math.round(start(f)), to = Math.round(start(f + 1));
      fill(text[f], from, Math.max(1, to - from - gap));
    }
    return;
  }
  // Several frames a column: each type there, the tallest behind.
  const columns = new Map();
  for (let f = 0; f < text.length; f++) {
    const c = Math.floor(start(f));
    let held = columns.get(c);
    if (!held) columns.set(c, held = new Set());
    held.add(text[f] in BARS ? text[f] : 'P');
  }
  for (const t of Object.keys(BARS)) {
    for (const [c, held] of columns) if (held.has(t)) fill(t, c, 1);
  }
}

function clear() {
  asked++;
  types = null;
  section.hidden = true;
  section.replaceChildren(section.firstElementChild);
  typesLane.show(false);
  typesLane.draw();
}

on('chose', () => { clear(); opened = null; });
on('opened', ({ video }) => { opened = video; });
on('run', clear);
on('done', async ({ video }) => {
  const mine = asked;
  let text;
  try {
    text = await call('FrameTypes');
  } catch (e) {
    if (e.name === 'AbortError' || mine !== asked) return;
    section.append(paragraph(`No frame types: ${e.message}`));
    section.hidden = false;
    return;
  }
  if (mine !== asked || video !== opened) return; // a new run or file meanwhile
  types = { text, video };
  typesLane.show(text.length > 0);
  typesLane.draw();
  fill(text);
});

function fill(text) {
  section.hidden = false;
  if (text.length === 0) {
    section.append(paragraph('No frames were read.'));
    return;
  }
  const counts = {};
  for (const t of text) counts[t] = (counts[t] ?? 0) + 1;
  const n = t => counts[t] ?? 0;
  const others = text.length - n('I') - n('P') - n('B');
  // One line, so the details' columns stay balanced: the counts, the core's; then the page's reading, the longest run
  // of B-frames (the bitrate's card has the keyframes' spacing).
  let longest = 0;
  for (let f = 0, run = 0; f < text.length; f++) longest = Math.max(longest, run = text[f] === 'B' ? run + 1 : 0);
  section.append(paragraph(`${count(text.length)} frames: ${count(n('I'))} I, ${count(n('P'))} P, ${count(n('B'))} B` +
    `${others > 0 ? `, ${count(others)} of other types` : ''}; `,
  own(longest === 0 ? 'no B-frames' : `at most ${longest} B in a row`,
    'The page\'s own: the longest run of B-frames in display order'), '.'));
}

const count = n => n.toLocaleString('en');

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
