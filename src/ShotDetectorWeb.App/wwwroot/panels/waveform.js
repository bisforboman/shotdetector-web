// The waveform, a lane of the timeline: the sound's envelope as ManagedCodecs' AAC decoder and FrameReader's
// WaveformBuilder give it (Api's Audio, after the file's first run), each point's lowest and highest sample on a linear
// scale, full scale the lane's height. A file without audio has no lane; audio the page can't measure (not AAC, or
// refused by the decoder) gets one line in it saying why.
import { on } from '../app.js';
import { lane, x } from '../timeline.js';

const SVG = 'http://www.w3.org/2000/svg';
// The core's words for a file without audio (Mp4Audio.Measure), which the page shows nothing for; tools/app-check.mjs
// checks every sample without audio gets exactly these.
const NO_AUDIO = 'The file has no audio track.';
let audio = null;  // Audio()'s JSON for the open file, once it has come

// One SVG over the points' span, in x()'s percentages: drawn again only for a new duration, not for a new width.
const waveLane = lane('Sound', track => {
  if (audio === null) return track.replaceChildren();
  if (audio.waveform === null) return track.replaceChildren(line(audio.notMeasured));
  track.replaceChildren(svg(audio.waveform));
});
waveLane.track.classList.add('waveform');
waveLane.label.classList.add('waveform');
waveLane.show(false);

// Each waveform (one, the channels averaged, unless split) in a band of its own: point i spans [i, i + 1] of the
// viewBox's width, the band's middle is silence, its edges full scale.
function svg({ start, pointsPerSecond, channels, min, max }) {
  const points = min[0].length, end = start + points / pointsPerSecond;
  const s = document.createElementNS(SVG, 'svg');
  s.setAttribute('viewBox', `0 0 ${points} ${2 * channels}`);
  s.setAttribute('preserveAspectRatio', 'none');
  s.setAttribute('aria-hidden', 'true');
  s.style.left = x(start);
  s.style.width = `calc(${x(end)} - ${x(start)})`;
  for (let c = 0; c < channels; c++) {
    const middle = 2 * c + 1, y = v => (middle - v / 32768).toFixed(4);
    let d = `M0 ${y(max[c][0])}`;
    for (let i = 0; i < points; i++) d += `H${i + 1}V${y(max[c][i + 1] ?? max[c][i])}`;
    for (let i = points - 1; i >= 0; i--) d += `V${y(min[c][i])}H${i}`;
    const path = document.createElementNS(SVG, 'path');
    path.setAttribute('d', `${d}Z`);
    s.append(path);
  }
  return s;
}

function line(text) {
  const p = document.createElement('span');
  p.className = 'not-measured';
  p.textContent = `No waveform: ${text}`;
  return p;
}

on('chose', () => {
  audio = null;
  waveLane.show(false);
  waveLane.draw();
});
on('audio', ({ audio: a }) => {
  audio = a;
  // Nothing at all for a file without audio; a line for audio that wasn't measured.
  waveLane.show(a.notMeasured !== NO_AUDIO);
  waveLane.draw();
});
