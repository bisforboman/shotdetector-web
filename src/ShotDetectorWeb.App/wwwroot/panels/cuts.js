// The cuts, a lane of the timeline: a tick where each shot after the first starts, as soon as the shot is known, and
// while a run goes, the part of the file it has read shaded (a cancelled run's stays, up to where it stopped).
import { on } from '../app.js';
import { lane, secondsOf, timecode, x } from '../timeline.js';

let cuts = [];  // the cuts' times in seconds
let read = 0;   // how far the run has read, in seconds (0 once it has finished: all of it)
const scanned = document.createElement('div');
scanned.className = 'scanned';

// Drawn whole when the axis changes; between, a new cut adds its tick and progress moves the shading.
const cutsLane = lane('Cuts', track => {
  scanned.style.width = x(read);
  track.replaceChildren(scanned, ...cuts.map(tick));
});

function tick(at) {
  const t = document.createElement('span');
  t.className = 'tick';
  t.style.left = x(at);
  t.title = `Cut at ${timecode(at)}`;
  return t;
}

function clear() {
  cuts = [];
  read = 0;
  cutsLane.draw();
}

on('chose', clear);
on('run', clear);
on('progress', ({ video: { frameRate: { num, den } }, done }) => {
  read = done * den / num;
  scanned.style.width = x(read);
});
on('shot', ({ shot }) => {
  if (shot.shot === 1) return;
  const at = secondsOf(shot.startTimecode);
  cuts.push(at);
  cutsLane.track.append(tick(at));
});
// The result's shots, which are the ones told while it ran.
on('done', ({ result }) => {
  cuts = result.shots.filter(s => s.shot > 1).map(s => secondsOf(s.startTimecode));
  read = 0;
  cutsLane.draw();
});
