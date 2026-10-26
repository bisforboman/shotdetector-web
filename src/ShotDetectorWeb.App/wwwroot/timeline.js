// The timeline under the <video> (decision 2026-10-25-one-page.md): one time axis that every lane shares, a playhead
// that follows the video, and a click on any lane seeks it. What happens over time on the page is a lane of it: a panel
// (panels/<name>.js) adds one with lane() and draws into its track, placing things with x(seconds). The axis is the
// file's, as Api's Open describes it (app.js sets it with setDuration).
const video = document.getElementById('video');
const timeline = document.getElementById('timeline');
const labels = timeline.querySelector('.labels'), tracks = timeline.querySelector('.tracks');
const axis = tracks.querySelector('.axis'), playhead = tracks.querySelector('.playhead');
const lanes = [];
let duration = 0;

// Adds a lane, below the ones added before it: its label, and a track the panel draws into. draw(track) is called
// whenever the axis changes (a new duration, a new width), and by the panel through the lane's draw() when its own
// data changes; a lane positioned in x()'s percentages only needs it for a new duration. show(false) hides the lane,
// label and track, for a file that has nothing for it (show(true) brings it back).
export function lane(label, draw) {
  const name = document.createElement('div');
  name.className = 'lane-label';
  name.textContent = label;
  labels.insertBefore(name, labels.lastElementChild);
  const track = document.createElement('div');
  track.className = 'track';
  tracks.insertBefore(track, axis);
  const entry = {
    track,
    label: name,
    draw: () => { if (duration > 0) draw(track); else track.replaceChildren(); },
    show: visible => { name.hidden = track.hidden = !visible; },
  };
  lanes.push(entry);
  return entry;
}

// Where a time is on the axis, as a CSS length (a percentage of the track's width).
export const x = seconds => `${Math.min(Math.max(seconds / duration, 0), 1) * 100}%`;

// The axis's length in seconds; 0 hides the timeline (no file open).
export function setDuration(seconds) {
  duration = seconds > 0 ? seconds : 0;
  timeline.hidden = duration === 0;
  tracks.setAttribute('aria-valuemax', duration.toFixed(3));
  redraw();
}

// Moves the video to a time: from a click on the timeline, or a panel's (a shot's row plays from there).
export function seek(at, { play = false } = {}) {
  video.currentTime = at;
  follow(); // now, not at the video's 'seeking'
  if (play) video.play().catch(() => {}); // a browser that can't play the file still shows where it is
}

// A timecode as ShotDetector prints them (HH:MM:SS.mmm) in seconds, and back.
export function secondsOf(tc) {
  const [h, m, s] = tc.split(':').map(Number);
  return h * 3600 + m * 60 + s;
}

export function timecode(s) {
  const ms = Math.round(s * 1000);
  const pad = (n, w) => String(n).padStart(w, '0');
  return `${pad(Math.floor(ms / 3600000), 2)}:${pad(Math.floor(ms / 60000) % 60, 2)}:${pad(Math.floor(ms / 1000) % 60, 2)}` +
    `.${pad(ms % 1000, 3)}`;
}

function redraw() {
  drawAxis();
  for (const l of lanes) l.draw();
  follow();
}

// Tick marks at a round step, as many as fit (one every 4.5rem or so).
const STEPS = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200];
function drawAxis() {
  axis.replaceChildren();
  if (duration === 0) return;
  const fit = Math.max(1, Math.floor(tracks.clientWidth / 72));
  const step = STEPS.find(s => duration / s <= fit) ?? STEPS.at(-1);
  for (let i = 0; i * step <= duration + 1e-9; i++) {
    const t = i * step, tick = document.createElement('span');
    tick.style.left = x(t);
    tick.textContent = clock(t, step);
    axis.append(tick);
  }
}

// An axis label: m:ss (h:mm:ss past an hour), with tenths when the step is under a second.
function clock(t, step) {
  const s = Math.round(t * 10) / 10, h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60;
  const sec = (s % 60).toFixed(step < 1 ? 1 : 0).padStart(step < 1 ? 4 : 2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
}

// The playhead and the slider's value follow the video.
function follow() {
  const t = video.currentTime || 0;
  playhead.style.left = x(t);
  playhead.hidden = duration === 0;
  tracks.setAttribute('aria-valuenow', t.toFixed(3));
  tracks.setAttribute('aria-valuetext', timecode(t));
}

for (const type of ['timeupdate', 'seeking', 'emptied']) video.addEventListener(type, follow);

tracks.addEventListener('click', e => {
  if (duration === 0) return;
  const box = tracks.getBoundingClientRect();
  seek(Math.min(Math.max((e.clientX - box.left) / box.width, 0), 1) * duration);
});

// The keyboard's way to the same: arrows a second (with Shift ten), Home and End.
tracks.addEventListener('keydown', e => {
  if (duration === 0) return;
  const t = video.currentTime || 0, step = e.shiftKey ? 10 : 1;
  const to = { ArrowLeft: t - step, ArrowRight: t + step, Home: 0, End: duration }[e.key];
  if (to === undefined) return;
  e.preventDefault();
  seek(Math.min(Math.max(to, 0), duration));
});

// A new width can fit more or fewer ticks, and a lane drawn in pixels (a canvas) has to be drawn again.
let width = 0;
new ResizeObserver(() => {
  if (tracks.clientWidth === width) return;
  width = tracks.clientWidth;
  redraw();
}).observe(tracks);

setDuration(0);
