// The picture's measures (backlog "Picture measures", decision 2026-10-30-picture-measures.md): every run measures each
// frame as it reads it (the core's PictureMeter; a split run each range in its worker), and Api's Picture gives them
// after the run. A lane of the timeline: each frame's luma, its lowest to highest as a band and its mean as a line, the
// black spans shaded, the colour bars marked, a tick where pixels leave broadcast range (a click on a span plays from
// its start). A card with the numbers. They are FFmpeg's figures, checked frame by frame against its signalstats,
// blackdetect and cropdetect, exactly (README, Checks): the card's mark says "measured". The colour bars are found by
// their definition (no FFmpeg filter finds them), checked on samples made with FFmpeg's smptebars and smptehdbars; what
// the page works out (the mean over the run, the bars' widths, a lane point over several frames) says so in its tooltip.
import { call, card, on } from '../app.js';
import { lane, seek, timecode, x } from '../timeline.js';

const SVG = 'http://www.w3.org/2000/svg';
const BAND = 0.15; // the out-of-range ticks' share of the lane's height, at its bottom

let picture = null; // the last run's Picture() JSON, with the video it is for
let opened = null;  // the opened file's YuvVideo
let asked = 0;      // how many runs began: a Picture() answered after a new run or file draws nothing

const section = card('Picture', {
  mark: 'measured',
  markTitle: 'The core\'s measures of every frame the run read: each checked frame by frame against FFmpeg\'s ' +
    'signalstats, blackdetect and cropdetect, exactly; the colour bars by their definition',
});
section.id = 'picture';
section.hidden = true;

const pictureLane = lane('Picture', track => {
  if (!picture) return track.replaceChildren();
  const { summary, video } = picture, seconds = f => f * video.frameRate.den / video.frameRate.num;
  track.replaceChildren(svg(summary, seconds),
    ...summary.black.map(s => mark('black', s, seconds, `Black: ${timecode(seconds(s.firstFrame))} to ` +
      `${timecode(seconds(s.endFrame))}, as blackdetect reports it from ${s.start} s to ${s.end} s`)),
    ...summary.bars.map(s => mark('bars', s, seconds, `Colour bars: ${timecode(seconds(s.firstFrame))} to ` +
      `${timecode(seconds(s.endFrame))}`)));
});
pictureLane.track.classList.add('picture');
pictureLane.label.classList.add('picture');
pictureLane.label.title = 'Each frame\'s luma (signalstats\' YMIN to YMAX as a band, YAVG as a line, 0 to 255), the ' +
  'black spans (blackdetect) shaded, the colour bars marked, a tick at the bottom where pixels are out of broadcast ' +
  'range (BRNG)';
pictureLane.show(false);

// The lane's points as an SVG over the run's frames, in x()'s percentages: x is the point, y the luma from 255 (top)
// to 0 above the ticks' band.
function svg({ frames, points: { framesPerPoint, min, mean, max, brng } }, seconds) {
  const n = min.length, s = document.createElementNS(SVG, 'svg');
  s.setAttribute('viewBox', `0 0 ${n} 1`);
  s.setAttribute('preserveAspectRatio', 'none');
  s.setAttribute('aria-hidden', 'true');
  s.style.left = x(0);
  s.style.width = x(seconds(frames));
  const y = v => ((1 - BAND) * (1 - v / 255)).toFixed(4);
  let band = `M0 ${y(max[0] ?? 0)}`;
  for (let i = 0; i < n; i++) band += `H${i + 1}V${y(max[i + 1] ?? max[i])}`;
  for (let i = n - 1; i >= 0; i--) band += `V${y(min[i])}H${i}`;
  let line = '';
  for (let i = 0; i < n; i++) line += `${i === 0 ? 'M' : 'L'}${i + 0.5} ${y(mean[i])}`;
  let ticks = '';
  for (let i = 0; i < n; i++) if (brng[i] > 0) ticks += `M${i + 0.5} ${1 - BAND}V1`;
  s.append(path('levels', `${band}Z`), path('mean', line), path('brng', ticks));
  if (framesPerPoint > 1) s.dataset.framesPerPoint = framesPerPoint;
  return s;
}

function path(kind, d) {
  const p = document.createElementNS(SVG, 'path');
  p.setAttribute('class', kind);
  p.setAttribute('d', d);
  return p;
}

function mark(kind, span, seconds, title) {
  const m = document.createElement('span');
  m.className = `mark ${kind}`;
  const start = seconds(span.firstFrame), end = seconds(Math.max(span.endFrame, span.firstFrame + 1));
  m.style.left = x(start);
  m.style.width = `calc(${x(end)} - ${x(start)})`;
  m.title = title;
  m.onclick = e => { e.stopPropagation(); seek(start, { play: true }); };
  return m;
}

function clear() {
  asked++;
  picture = null;
  section.hidden = true;
  section.replaceChildren(section.firstElementChild);
  pictureLane.show(false);
  pictureLane.draw();
}

on('chose', () => { clear(); opened = null; });
on('opened', ({ video }) => { opened = video; });
on('run', clear);
on('done', async ({ video }) => {
  const mine = asked;
  let json;
  try {
    json = await call('Picture');
  } catch (e) {
    if (e.name === 'AbortError' || mine !== asked) return;
    section.append(paragraph(`The picture wasn't measured: ${e.message}`));
    section.hidden = false;
    return;
  }
  if (mine !== asked || video !== opened) return; // a new run or file meanwhile
  picture = { summary: JSON.parse(json), video };
  pictureLane.show(picture.summary.frames > 0);
  pictureLane.draw();
  fill(picture);
});

function fill({ summary: p, video }) {
  const seconds = f => f * video.frameRate.den / video.frameRate.num;
  const at = f => `${clock(seconds(f))}`;
  section.hidden = false;
  if (p.frames === 0) {
    section.append(paragraph('No frames were read.'));
    return;
  }

  section.append(heading('Luma', 'signalstats\' YMIN, YAVG and YMAX of every frame, checked against it exactly'));
  const list = document.createElement('dl');
  list.className = 'levels';
  for (const [term, text, title] of [
    ['Lowest', String(p.levels.min), 'The lowest YMIN of the frames read'],
    ['Highest', String(p.levels.max), 'The highest YMAX of the frames read'],
    ['Mean', own(p.levels.mean.toFixed(1), `The page's own: the mean of the frames' YAVG, ${p.levels.mean}`), ''],
  ]) {
    const dt = document.createElement('dt'), dd = document.createElement('dd');
    dt.textContent = term;
    dd.append(text);
    if (title) dd.title = title;
    list.append(dt, dd);
  }
  section.append(list);

  section.append(heading('Broadcast range', 'signalstats\' BRNG: luma outside 16 to 235 or chroma outside 16 to 240, ' +
    'checked against it exactly'));
  if (p.brng.frames === 0) section.append(paragraph('Every pixel of every frame in range.'));
  else {
    section.append(paragraph(`Out of range in ${count(p.brng.frames)} of ${count(p.frames)} frames; at most ` +
      `${percent(p.brng.largest)} of a frame (frame ${count(p.brng.at + 1)}, ${at(p.brng.at)}).`,
    `BRNG ${p.brng.largest} at frame ${p.brng.at + 1} (${timecode(seconds(p.brng.at))})`));
  }
  if (p.fullRange) section.append(paragraph('The video is full range (0 to 255): BRNG counts against broadcast range anyway.'));

  section.append(heading('Black', `blackdetect: 98% of a frame's pixels at or under ${p.fullRange ? 25 : 37}, ` +
    'checked against it exactly; it lists the spans of 2 s or more by default'));
  const long = p.black.filter(s => s.long), short = p.black.length - long.length;
  const shorter = `${short} shorter, on the timeline`;
  if (long.length === 0) section.append(paragraph(`Black: none of 2 s or more${short > 0 ? `; ${shorter}` : ''}.`));
  for (const s of long) {
    section.append(paragraph(`Black: ${at(s.firstFrame)} to ${at(s.endFrame)}`, `blackdetect: ${s.start} s to ${s.end} s`));
  }
  if (long.length > 0 && short > 0) section.append(paragraph(`Shorter than 2 s: ${short}, on the timeline.`));

  section.append(heading('Letterbox', 'cropdetect with its defaults (limit 24, round 16, the first two frames skipped), ' +
    'checked against it exactly'));
  if (p.crop === null) section.append(paragraph('Too few frames for cropdetect.'));
  else {
    const { x1, x2, y1, y2 } = p.crop, w = video.width, h = video.height;
    const bars = [['top', y1], ['bottom', h - 1 - y2], ['left', x1], ['right', w - 1 - x2]].filter(([, n]) => n > 0);
    const code = document.createElement('code');
    code.textContent = `crop=${p.crop.w}:${p.crop.h}:${p.crop.x}:${p.crop.y}`;
    section.append(paragraph(bars.length === 0
      ? 'No black bars: there is content at every edge.'
      : own(`Black bars: ${bars.map(([side, n]) => `${n} px ${side}`).join(', ')}; the picture ` +
        `${x2 - x1 + 1}×${y2 - y1 + 1} (${((x2 - x1 + 1) / (y2 - y1 + 1)).toFixed(2)}:1).`,
      `The page's own, from cropdetect's x1 ${x1}, x2 ${x2}, y1 ${y1}, y2 ${y2}: the edges with no content in any frame`)));
    section.append(paragraph(['cropdetect: ', code]));
  }

  section.append(heading('Colour bars', 'seven bars side by side, white to blue at 75% or 100%, in BT.601 or BT.709 ' +
    '(SMPTE and EBU bars): by their definition, checked on FFmpeg\'s smptebars and smptehdbars'));
  if (p.bars.length === 0) section.append(paragraph('Colour bars: none.'));
  for (const s of p.bars) {
    section.append(paragraph(`Colour bars: ${at(s.firstFrame)} to ${at(s.endFrame)}`,
      `Frames ${s.firstFrame + 1} to ${s.endFrame}, ${timecode(seconds(s.firstFrame))} to ${timecode(seconds(s.endFrame))}`));
  }

  const note = paragraph('Measured: every frame the run read, FFmpeg\'s figures exactly; times rounded to the second ' +
    'here, exact in each one\'s tooltip and on the timeline.');
  note.className = 'note';
  section.append(note);
}

const count = n => n.toLocaleString('en');
const percent = r => `${Number((r * 100).toPrecision(2))}%`;

// m:ss, h:mm:ss past an hour, to the nearest second.
function clock(seconds) {
  const s = Math.round(seconds), h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60;
  const ss = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

function own(text, title) {
  const span = document.createElement('span');
  span.className = 'own';
  span.textContent = text;
  span.title = title;
  return span;
}

function paragraph(text, title = '') {
  const p = document.createElement('p');
  p.append(...(Array.isArray(text) ? text : [text]));
  if (title) p.title = title;
  return p;
}

// A heading, how its measure is checked in its tooltip (the card's mark says "measured").
function heading(text, how) {
  const h = document.createElement('h3');
  h.textContent = text;
  h.title = `The core's measure: ${how}`;
  return h;
}
