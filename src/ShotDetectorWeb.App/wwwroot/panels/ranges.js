// The ranges, a lane of the timeline: a run split across workers (decision 2026-10-26-workers.md) as one band per
// worker's range, from its first frame to the next one's, filled as far as that worker has got (the shots come only once
// the ranges merge); a stopped run's ranges stay where each got to. A run on one worker has no lane, but for one line
// when a split run's ranges didn't merge and it was detected again with one worker, saying why.
import { frameSeconds, on } from '../app.js';
import { lane, x } from '../timeline.js';

let ranges = null;   // the split run's ranges, [{ firstFrame, endFrame, done }], or null
let video = null;    // the video it runs on (frameSeconds)
let fallback = null; // why the run went back to one worker

// The bands in x()'s percentages: drawn again only for a new duration or a new run, their fill moved by progress.
const rangesLane = lane('Ranges', track => {
  if (fallback) {
    const p = document.createElement('span');
    p.className = 'not-measured';
    p.textContent = `One worker: ${fallback}`;
    return track.replaceChildren(p);
  }
  track.replaceChildren(...(ranges ?? []).map((r, i) => {
    const band = document.createElement('div'), fill = document.createElement('div');
    band.className = 'range';
    const from = frameSeconds(video, r.firstFrame), to = frameSeconds(video, r.endFrame);
    band.style.left = x(from);
    band.style.width = `calc(${x(to)} - ${x(from)})`;
    band.title = `Worker ${i + 1}: frames ${r.firstFrame} to ${r.endFrame - 1}`;
    fill.className = 'done';
    band.append(fill);
    r.fill = fill;
    paint(r);
    return band;
  }));
});
rangesLane.track.classList.add('ranges');
rangesLane.show(false);

function paint(r) {
  r.fill?.style.setProperty('width', `${Math.min(r.done / (r.endFrame - r.firstFrame), 1) * 100}%`);
}

on('chose', () => {
  ranges = fallback = null;
  rangesLane.show(false);
  rangesLane.draw();
});
on('run', ({ video: v, ranges: planned, fallback: why }) => {
  ranges = planned?.map(r => ({ ...r, done: 0 })) ?? null;
  video = v;
  fallback = why;
  rangesLane.show(ranges !== null || fallback !== null);
  rangesLane.draw();
});
on('range', ({ index, done }) => {
  if (!ranges?.[index]) return;
  ranges[index].done = done;
  paint(ranges[index]);
});
