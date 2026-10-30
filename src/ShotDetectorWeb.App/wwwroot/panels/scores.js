// The scores, a lane of the timeline: each frame's score from the run's stats CSV (Api's StatsCsv, the 'done' result's
// statsCsv) on a <canvas>, the threshold the run compared it against as a dashed line, and a mark at each cut with a dot
// on the score there, so a cut shows why it was found. Where the frame difference (content_val) stays near zero for a
// second or more, the lane marks a still picture, as the page's reading (ShotDetector says nothing of stills). Nothing
// here decides a cut: the cuts are the run's, and a new threshold is a new run.
//
// A two-hour film's stats CSV is about 20 million characters (README "What the heap holds"), a Blob from the worker: its
// text is read once, when the run is done, into a typed array of times and one of scores (and the still spans), and
// each draw takes, for every column of the canvas's pixels, the frames' first, lowest, highest and last score there.
import { on } from '../app.js';
import { lane, secondsOf, timecode, x } from '../timeline.js';

// Each detector's column in the stats CSV, by the start of its header (the header goes on with the detector's
// parameters: "adaptive_ratio (w=2)", "hash_dist [size=8 lowpass=2]"), the threshold the run used when the page sent
// none (ShotDetector's default, as scenedetect 0.7.2's: DetectionOptions.Compatibility), where that threshold is on the
// column's scale, and how the detector compares them (the lane's legend says it).
const DETECTORS = {
  adaptive: { column: 'adaptive_ratio', threshold: 3, line: t => t,
    rule: (t, { minContentVal }) => `a cut where it is ${t} or more and content_val is ${minContentVal ?? 15} or more` },
  content: { column: 'content_val', threshold: 27, line: t => t,
    rule: t => `a cut where it is ${t} or more` },
  // The detector truncates its threshold to an integer (scenedetect's int(threshold)).
  threshold: { column: 'average_rgb', threshold: 12, line: t => Math.trunc(t),
    rule: t => `a fade where it is under ${Math.trunc(t)}, the cut between the fade out and the fade in` },
  // hist_diff is the correlation of a frame's luma histogram with the last's: a cut where it is 1 - threshold or less.
  histogram: { column: 'hist_diff', threshold: 0.2, line: t => Math.min(Math.max(1 - t, 0), 1),
    rule: t => `a cut where it is ${Number((1 - t).toFixed(6))} or less (1 - ${t})` },
  hash: { column: 'hash_dist', threshold: 0.35, line: t => t,
    rule: t => `a cut where it is ${t} or more` },
};

// A still picture, the page's reading: content_val (the mean change of hue, saturation and luma, 0 to 255) under STILL
// for STILL_SECONDS or more. 1 holds the Sintel trailer's title cards and its opening black, and nothing that moves.
const STILL = 1, STILL_SECONDS = 1;
// The scale's top is the highest score, but at most this many times the line, so a few huge ratios (adaptive_ratio
// goes up to 255) don't flatten the rest: a higher score is drawn at the top.
const MAX_LINES = 4;
// The least average spacing of the cuts, in CSS pixels, for the lane to mark them.
const CUT_SPACING = 4;

let graph = null;  // the last finished run's: { name, kind, threshold, line, rule, times, scores, low, high, cuts, stills }
let note = null;   // why there is no graph, for a run whose stats CSV doesn't have the column

const canvas = document.createElement('canvas');
canvas.setAttribute('aria-hidden', 'true');
const legend = document.createElement('span');
legend.className = 'legend';
const level = document.createElement('span');
level.className = 'level';

const scoresLane = lane('Scores', draw);
scoresLane.track.classList.add('scores');
scoresLane.label.classList.add('scores');
scoresLane.show(false);

let run = null;  // what the current run compares (runOf), from its options
let cleared = 0; // how many times the lane was cleared: a 'done' whose CSV is read after a clear draws nothing

on('chose', clear);
on('run', ({ options }) => {
  clear();
  run = runOf(options);
});
on('done', async ({ result }) => {
  const asked = cleared;
  const csv = await result.statsCsv.text();
  if (cleared !== asked) return; // a new run or file meanwhile
  const data = read(csv, run);
  if (typeof data === 'string') note = data;
  else graph = { ...run, ...data, cuts: result.shots.filter(s => s.shot > 1).map(s => secondsOf(s.startTimecode)) };
  scoresLane.show(true);
  scoresLane.draw();
});
// The colours are the page's (style.css), which change with the system's light or dark.
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => scoresLane.draw());

function clear() {
  cleared++;
  graph = null;
  note = null;
  scoresLane.show(false);
  scoresLane.draw();
}

// What the run compared, from the options the page sent: the detector, and the threshold (none: the default).
function runOf(options) {
  const { detector, threshold } = options;
  const d = DETECTORS[detector];
  const t = threshold ?? d.threshold;
  return { kind: detector, threshold: t, line: d.line(t), rule: d.rule(t, options), column: d.column };
}

// The stats CSV, once: the frames' times (their timecodes) and the run's scores, both as typed arrays, the lowest and
// highest score, and the still spans from content_val where the CSV has it (adaptive's and content's). A string for a
// CSV without the column. Fields are found by the header's names; a score of None (no score yet, adaptive's first
// frames) is NaN and drawn as nothing.
function read(csv, { column }) {
  let end = csv.indexOf('\n');
  const header = csv.slice(0, end).replace(/\r$/, '').split(',');
  const at = name => header.findIndex(h => h === name || h.startsWith(`${name} `));
  const timeAt = at('Timecode'), scoreAt = at(column), stillAt = at('content_val');
  if (timeAt < 0 || scoreAt < 0) return `The stats CSV has no ${scoreAt < 0 ? column : 'Timecode'} column.`;
  const name = header[scoreAt];

  let rows = 0;
  for (let i = end; i !== -1 && i < csv.length - 1; i = csv.indexOf('\n', i + 1)) rows++;
  const times = new Float64Array(rows), scores = new Float32Array(rows);
  const still = stillAt >= 0 ? new Float32Array(rows) : null;
  const last = Math.max(timeAt, scoreAt, stillAt);
  let low = Infinity, high = -Infinity;
  for (let r = 0, start = end + 1; r < rows; r++, start = end + 1) {
    end = csv.indexOf('\n', start);
    if (end === -1) end = csv.length;
    // The fields up to the last one wanted, by their commas (no field of the stats CSV is quoted).
    for (let f = 0, from = start; f <= last; f++) {
      let to = csv.indexOf(',', from);
      if (to === -1 || to > end) to = end;
      if (f === timeAt) times[r] = seconds(csv, from, to);
      else if (f === scoreAt || f === stillAt) {
        const v = Number(csv.slice(from, to)); // "None" is NaN
        if (f === scoreAt) {
          scores[r] = v;
          if (v < low) low = v;
          if (v > high) high = v;
        }
        if (f === stillAt) still[r] = v;
      }
      from = to + 1;
    }
  }
  return { name, times, scores, low, high, stills: still ? stills(times, still) : [] };
}

// A timecode as the stats CSV writes it (HH:MM:SS.mmm), from its characters, without a string of its own.
function seconds(csv, from, to) {
  let t = 0, part = 0, scale = 0;
  for (let i = from; i < to; i++) {
    const c = csv.charCodeAt(i);
    if (c === 58) { t = (t + part) * 60; part = 0; }  // ':'
    else if (c === 46) scale = 1;                     // '.'
    else if (scale > 0) part += (c - 48) * (scale /= 10);
    else part = part * 10 + c - 48;
  }
  return t + part;
}

// The spans where content_val stays under STILL for STILL_SECONDS or more: content_val is a frame's change from the one
// before, so a run of them from frame a to frame b is a picture still from a's frame before to b's end.
function stills(times, contentVal) {
  const spans = [], frame = frameLength(times);
  for (let i = 0; i < times.length; i++) {
    if (!(contentVal[i] < STILL)) continue;
    let j = i;
    while (j + 1 < times.length && contentVal[j + 1] < STILL) j++;
    const start = Math.max(times[i] - frame, 0), end = times[j] + frame;
    if (end - start >= STILL_SECONDS) spans.push([start, end]);
    i = j;
  }
  return spans;
}

function draw(track) {
  if (note !== null) return track.replaceChildren(line(note));
  if (graph === null) return track.replaceChildren();
  const { name, kind, line: at, rule, times, scores, low, high, cuts, stills } = graph;
  const width = track.clientWidth, height = track.clientHeight, ratio = devicePixelRatio || 1;
  canvas.width = Math.round(width * ratio);
  canvas.height = Math.round(height * ratio);
  const w = canvas.width, h = canvas.height, pad = Math.round(3 * ratio);

  // The scale: from the lowest score (0 unless one is under it) to the highest, the line within it.
  const bottom = Math.min(0, low, at);
  let top = Math.max(high, at);
  if (at > 0) top = Math.min(top, Math.max(at * MAX_LINES, at * 1.25));
  if (!(top > bottom)) top = bottom + 1;
  const y = v => pad + (h - 2 * pad) * (1 - (Math.min(Math.max(v, bottom), top) - bottom) / (top - bottom));
  // Where a time is on the axis, in the canvas's pixels: x() is a percentage of the track's width, linear from 0.
  const end = times.length > 0 ? times[times.length - 1] : 0;
  const perSecond = end > 0 ? parseFloat(x(end)) / 100 / end * w : 0;
  const px = t => t * perSecond;

  const style = getComputedStyle(track);
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, w, h);

  // The cuts, a line across the lane each, while they are CUT_SPACING pixels apart on average: closer (a film's thousand
  // cuts at phone width), they would cover the lane, and the cuts lane above has them.
  const marked = cuts.length * CUT_SPACING * ratio <= w ? cuts : [];
  ctx.fillStyle = style.getPropertyValue('--score-cut');
  for (const c of marked) ctx.fillRect(Math.round(px(c) - ratio / 2), 0, Math.max(1, Math.round(ratio)), h);

  // The scores: per pixel column, the first, lowest, highest and last score of its frames, joined into one line, so a
  // single frame's spike (or dip) shows at any width.
  ctx.beginPath();
  let column = -1, first = 0, min = 0, max = 0, lastScore = 0, open = false;
  const flush = () => {
    if (column < 0) return;
    const cx = column + 0.5;
    if (open) ctx.lineTo(cx, y(first)); else ctx.moveTo(cx, y(first));
    ctx.lineTo(cx, y(min));
    ctx.lineTo(cx, y(max));
    ctx.lineTo(cx, y(lastScore));
    open = true;
  };
  for (let i = 0; i < times.length; i++) {
    const v = scores[i];
    if (Number.isNaN(v)) { flush(); column = -1; open = false; continue; }
    const c = Math.min(Math.floor(px(times[i])), w - 1);
    if (c !== column) {
      flush();
      column = c;
      first = min = max = v;
    }
    if (v < min) min = v;
    if (v > max) max = v;
    lastScore = v;
  }
  flush();
  ctx.lineJoin = 'round';
  ctx.lineWidth = 1.25 * ratio;
  ctx.strokeStyle = style.getPropertyValue('--score');
  ctx.stroke();

  // The threshold.
  ctx.beginPath();
  ctx.setLineDash([4 * ratio, 3 * ratio]);
  ctx.moveTo(0, Math.round(y(at)) + 0.5);
  ctx.lineTo(w, Math.round(y(at)) + 0.5);
  ctx.lineWidth = Math.max(1, Math.round(ratio));
  ctx.strokeStyle = style.getPropertyValue('--score-threshold');
  ctx.stroke();
  ctx.setLineDash([]);

  // A dot on each cut's score, its first frame's: on or past the line for a cut the score made.
  ctx.fillStyle = style.getPropertyValue('--score-cut');
  for (const c of marked) {
    const i = frameAt(times, c);
    if (i < 0 || Number.isNaN(scores[i])) continue;
    ctx.beginPath();
    ctx.arc(px(c), y(scores[i]), 2.5 * ratio, 0, 2 * Math.PI);
    ctx.fill();
  }

  legend.textContent = name;
  legend.title = `${name}, each frame's score in the stats CSV (${kind}): ${rule}. ` +
    `Scale ${number(bottom)} to ${number(top)}${high > top ? `, higher scores (up to ${number(high)}) at the top` : ''}.`;
  level.textContent = number(at);
  level.title = `The threshold: ${rule}`;
  level.style.top = `${y(at) / h * 100}%`;
  level.classList.toggle('under', y(at) < h / 3);
  track.replaceChildren(...stills.map(([s, e]) => still(s, e)), canvas, legend, level);
}

// The frame whose time is t (a cut's start), by binary search over the times; -1 if none is within half a frame.
function frameAt(times, t) {
  let lo = 0, hi = times.length - 1;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (times[mid] < t) lo = mid + 1; else hi = mid;
  }
  const best = lo > 0 && Math.abs(times[lo - 1] - t) < Math.abs(times[lo] - t) ? lo - 1 : lo;
  return Math.abs(times[best] - t) < (frameLength(times) || Infinity) / 2 ? best : -1;
}

// A frame's length: the timecodes' average step (each is rounded to the millisecond; the frame rate is constant).
const frameLength = times => times.length > 1 ? (times[times.length - 1] - times[0]) / (times.length - 1) : 0;

function still(start, end) {
  const s = document.createElement('span');
  s.className = 'still';
  s.style.left = x(start);
  s.style.width = `calc(${x(end)} - ${x(start)})`;
  s.title = `A still picture, the page's reading: content_val under ${STILL} from ${timecode(start)} to ${timecode(end)}` +
    ` (${(end - start).toFixed(1)} s)`;
  const label = document.createElement('span');
  label.textContent = 'still';
  s.append(label);
  return s;
}

function line(text) {
  const p = document.createElement('span');
  p.className = 'not-measured';
  p.textContent = `No graph: ${text}`;
  return p;
}

const number = v => String(Number(v.toPrecision(4)));
