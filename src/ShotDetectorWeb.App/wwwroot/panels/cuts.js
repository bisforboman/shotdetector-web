// The cuts, a lane of the timeline: a tick where each shot after the first starts, as soon as the shot is known, and
// while a run goes, the part of the file it has read shaded (a stopped or lost run's stays, up to where it stopped).
// Behind the ticks, each shot is a span: the one playing (app.js's 'playing') shaded, its number on it where the span is
// wide enough to hold it, and its number and times in its tooltip.
import { on } from '../app.js';
import { lane, secondsOf, timecode, x } from '../timeline.js';

const LABEL_PX = 22; // the narrowest span that shows its shot's number

let cuts = [];  // the cuts' times in seconds
let shots = []; // the shots known: { shot, start, end, length } in seconds
let playing = 0; // the shot playing's number, 0 for none
let read = 0;   // how far the run has read, in seconds (0 once it has read all of it)
// The track's width in pixels, read once per whole drawing (which the axis's changes, its width too, bring): reading it
// for each span would lay the page out once per shot, seconds of the page's thread when a split run's merge tells a
// long film's thousand shots at once (backlog "Performance pass on the page").
let width = 0;
const scanned = document.createElement('div');
scanned.className = 'scanned';

// Drawn whole when the axis changes (its width too: which spans hold their number); between, a new shot adds its span
// and tick and progress moves the shading.
const cutsLane = lane('Cuts', track => {
  scanned.style.width = x(read);
  width = track.clientWidth;
  track.replaceChildren(scanned, ...shots.map(span), ...cuts.map(tick));
});

function tick(at) {
  const t = document.createElement('span');
  t.className = 'tick';
  t.style.left = x(at);
  t.title = `Cut at ${timecode(at)}`;
  return t;
}

function span({ shot, start, end, length }) {
  const s = document.createElement('span');
  s.className = `shot${shot === playing ? ' current' : ''}`;
  s.dataset.shot = shot;
  s.style.left = x(start);
  s.style.width = `calc(${x(end)} - ${x(start)})`;
  s.title = `Shot ${shot}: ${timecode(start)} to ${timecode(end)}, ${length.toFixed(3)} s`;
  // x() is a share of the track's width: the span's own width in pixels, for whether its number fits.
  if (width * (parseFloat(x(end)) - parseFloat(x(start))) / 100 >= LABEL_PX) s.textContent = shot;
  return s;
}

function told(shot) {
  return { shot: shot.shot, start: secondsOf(shot.startTimecode), end: secondsOf(shot.endTimecode),
    length: shot.durationSeconds };
}

function clear() {
  cuts = [];
  shots = [];
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
  const s = told(shot);
  // A resumed run tells the stopped part's last shot again, to where it ends: it replaces the one cut short, drawn whole.
  if (shots.length >= s.shot) {
    shots.length = s.shot - 1;
    shots.push(s);
    cuts = shots.filter(o => o.shot > 1).map(o => o.start);
    return cutsLane.draw();
  }
  shots.push(s);
  // Drawn while the timeline was hidden: its width once, now.
  if (width === 0) width = cutsLane.track.clientWidth;
  cutsLane.track.insertBefore(span(s), scanned.nextSibling);
  if (s.shot === 1) return;
  cuts.push(s.start);
  cutsLane.track.append(tick(s.start));
});
// The result's shots, which are the ones told while it ran; a stopped run's, the part read.
on('done', ({ result }) => {
  shots = result.shots.map(told);
  cuts = shots.filter(s => s.shot > 1).map(s => s.start);
  read = result.stopped ? result.frameCount * result.frameRate.den / result.frameRate.num : 0;
  cutsLane.draw();
});
on('playing', ({ shot }) => {
  const number = shot?.shot ?? 0;
  if (number === playing) return;
  playing = number;
  for (const s of cutsLane.track.querySelectorAll('.shot')) s.classList.toggle('current', Number(s.dataset.shot) === number);
});
