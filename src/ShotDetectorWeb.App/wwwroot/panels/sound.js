// The sound's measures (backlog "Audio measures"), from the audio event the waveform's export brings after a run: a lane
// of the timeline with the reference tone's spans and the silences (a click on one plays from its start), and a card
// with the numbers. They are the core's (Mp4Audio, ManagedCodecs' AAC decoder in the worker), each checked against
// FFmpeg's ebur128 and silencedetect or ShotDetector's FindReferenceTone within a tolerance (README, Checks): the card
// says "measured". What the page works out from them (a silence's length) it marks as its own. The lane shows only when
// there is a tone or a silence to mark. A file without an audio track shows nothing; one whose audio isn't measured, the
// one line saying why.
import { card, on } from '../app.js';
import { lane, seek, timecode, x } from '../timeline.js';

// Mp4Audio's reason for a file without audio (AudioMeasures.Without in Mp4Audio.cs), the one reason that shows nothing.
const NO_AUDIO_TRACK = 'The file has no audio track.';
// How many silences the card lists; the lane has them all.
const LISTED = 10;

let audio = null; // the last audio event's object, while it was measured

const section = card('Sound');
section.id = 'sound';
section.hidden = true;

const soundLane = lane('Audio', track => {
  track.replaceChildren(
    ...(audio?.referenceTone ?? []).map(s => mark('tone', s, `Reference tone: ${tone(s)}. Play from ${timecode(s.start)}`)),
    ...(audio?.silences ?? []).map(s => mark('silence', s, `Silence: ${timecode(s.start)} to ${timecode(s.end)}. Play from ${timecode(s.start)}`)));
});

function mark(kind, { start, end }, title) {
  const m = document.createElement('span');
  m.className = `mark ${kind}`;
  m.style.left = x(start);
  m.style.width = `calc(${x(end)} - ${x(start)})`;
  m.title = title;
  m.onclick = e => { e.stopPropagation(); seek(start, { play: true }); };
  return m;
}

function clear() {
  audio = null;
  section.hidden = true;
  section.replaceChildren(section.firstElementChild);
  soundLane.show(false);
  soundLane.draw();
}

on('chose', clear);
on('audio', a => {
  clear();
  if (a.notMeasured === NO_AUDIO_TRACK) return;
  section.hidden = false;
  if (a.notMeasured != null || a.track == null) {
    section.append(paragraph(a.notMeasured ?? 'The sound wasn\'t measured.'));
    return;
  }
  audio = a;
  soundLane.show(a.referenceTone.length + a.silences.length > 0);
  soundLane.draw();
  fill(a);
});

function fill({ track, referenceTone, silences, peaks, loudness }) {
  section.append(paragraph(`${track.codec.toUpperCase()}, ${Number((track.sampleRate / 1000).toFixed(3))} kHz, ` +
    `${track.channels} ${track.channels === 1 ? 'channel' : 'channels'}, ${track.duration.toFixed(2)} s` +
    `${track.start > 0 ? ` from ${timecode(track.start)}` : ''}.`));

  section.append(heading('Loudness', 'EBU R128 (ITU-R BS.1770-4), checked against FFmpeg\'s ebur128 within 0.1 LU and 0.1 dB'));
  const list = document.createElement('dl');
  list.className = 'loudness';
  const integrated = loudness?.integrated == null
    ? ['nothing above the gate', 'Nothing is louder than the absolute gate, -70 LUFS']
    : [`${loudness.integrated.toFixed(1)} LUFS`, `${loudness.integrated} LUFS`];
  for (const [term, [text, title]] of [
    ['Integrated', integrated],
    ['Range', loudness == null ? ['–', ''] : [`${loudness.range.toFixed(1)} LU`, `${loudness.range} LU`]],
    ['True peak', loudness == null ? ['–', ''] : level(loudness.truePeak, 'dBTP')],
  ]) {
    const dt = document.createElement('dt'), dd = document.createElement('dd');
    dt.textContent = term;
    dd.textContent = text;
    dd.title = title;
    list.append(dt, dd);
  }
  section.append(list);

  section.append(heading('Peaks', 'per channel, in the decoder\'s order; checked against FFmpeg\'s ebur128 within 0.1 dB'));
  const table = document.createElement('table');
  table.className = 'peaks';
  table.innerHTML = '<thead><tr><th>Channel</th><th>Peak</th><th>True peak</th><th>At full scale</th></tr></thead><tbody></tbody>';
  peaks.forEach((p, c) => {
    const row = table.tBodies[0].insertRow();
    for (const [text, title] of [
      [String(c + 1), ''],
      level(p.sample, 'dBFS'),
      level(p.true, 'dBTP'),
      [p.fullScale.toLocaleString('en'), `${p.fullScale} ${p.fullScale === 1 ? 'sample reaches' : 'samples reach'} full scale`],
    ]) {
      const cell = row.insertCell();
      cell.textContent = text;
      cell.title = title;
    }
  });
  section.append(table);

  section.append(heading('Reference tone', 'mostly 1 kHz at a steady level for 5 s or more, as ShotDetector\'s FindReferenceTone, checked against it'));
  if (referenceTone.length === 0) section.append(paragraph('Reference tone: none.'));
  for (const s of referenceTone) section.append(paragraph(`Reference tone: ${tone(s)}`, span(s)));

  section.append(heading('Silence', 'every channel below -60 dBFS for 2 s or more, as FFmpeg\'s silencedetect, checked against it'));
  if (silences.length === 0) section.append(paragraph('Silence: none of 2 s or more.'));
  for (const s of silences.slice(0, LISTED)) {
    const p = paragraph(`Silence: ${clock(s.start)} to ${clock(s.end)}, `, span(s));
    const length = document.createElement('span');
    length.className = 'own';
    length.textContent = `${(s.end - s.start).toFixed(1)} s`;
    length.title = 'The page\'s own: the end minus the start';
    p.append(length);
    section.append(p);
  }
  if (silences.length > LISTED) section.append(paragraph(`and ${silences.length - LISTED} more, on the timeline.`));

  const note = paragraph('Measured: the numbers and times above are the core\'s, checked against FFmpeg and ShotDetector ' +
    'within a tolerance; times rounded to the second here, exact in each one\'s tooltip and on the timeline. ');
  note.className = 'note';
  const own = document.createElement('span');
  own.className = 'own';
  own.textContent = 'Underlined dotted: the page\'s own.';
  note.append(own);
  section.append(note);
}

// "1 kHz at -20.0 dBFS, 0:00 to 0:30": 1 kHz is what FindReferenceTone looks for, not a measure of the segment's.
const tone = s => `1 kHz at ${s.level.toFixed(1)} dBFS, ${clock(s.start)} to ${clock(s.end)}`;

// A line's exact times, as a tooltip.
const span = s => `${timecode(s.start)} to ${timecode(s.end)}`;

// A level in dB and its tooltip: an all-zero channel's is -infinity, which JSON carries as null or a string.
function level(v, unit) {
  const n = typeof v === 'number' ? v : -Infinity;
  return Number.isFinite(n) ? [`${n.toFixed(1)} ${unit}`, `${n} ${unit}`] : [`-∞ ${unit}`, 'Silent throughout'];
}

// m:ss, h:mm:ss past an hour, to the nearest second.
function clock(seconds) {
  const s = Math.round(seconds), h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60;
  const ss = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

function paragraph(text, title = '') {
  const p = document.createElement('p');
  p.textContent = text;
  if (title) p.title = title;
  return p;
}

function heading(text, how) {
  const h = document.createElement('h3');
  h.textContent = `${text} `;
  const tag = document.createElement('span');
  tag.className = 'measured';
  tag.textContent = 'measured';
  tag.title = `The core's measure: ${how}`;
  h.append(tag);
  return h;
}
