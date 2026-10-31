// The shots' statistics, a card beside the shots table (backlog "Shot statistics and a timeline"): from the shots as
// Detect returns them, how many, their average and median length, the shortest and the longest (a click plays it), cuts
// per minute and how long the run took against the length it read. They are the page's own figures, worked out here
// from the shots, not ShotDetector's, and the card's mark says so. They wait for a finished run. The shot playing is
// app.js's caption under the video, and the shots table marks its row.
import { card, on } from '../app.js';
import { secondsOf, seek } from '../timeline.js';

const section = card('Shot statistics', {
  place: 'below',
  mark: 'page',
  markTitle: 'Worked out by the page from the shots ShotDetector found, not ShotDetector\'s numbers: their lengths as in ' +
    'the scenes CSV, a cut where each shot after the first starts, the length read from the first shot\'s start to the ' +
    'last one\'s end. Exact values in the tooltips',
});
section.id = 'stats';
section.hidden = true;
const list = document.createElement('dl');
section.append(list);

let shots = [];   // the result's shots
let starts = [];  // each shot's start in seconds, and its end
let ends = [];

function clear() {
  shots = [];
  starts = [];
  ends = [];
  section.hidden = true;
  list.replaceChildren();
}

function add(shot) {
  shots.push(shot);
  starts.push(secondsOf(shot.startTimecode));
  ends.push(secondsOf(shot.endTimecode));
}

on('chose', clear);
on('run', clear);
on('done', ({ video: { frameRate: { num, den } }, result, seconds, from, restored }) => {
  clear();
  for (const shot of result.shots) add(shot);
  if (shots.length === 0) return;
  // A stopped run shown again ran in an earlier visit; a resumed one read the video from its rest's first frame (the
  // status line's times real time is of the same frames).
  fill(restored ? null : seconds, (result.frameCount - from) * den / num, result.stopped);
  section.hidden = false;
});

// seconds: the run's, null for none; read: the video it read in that time; stopped: the last shot is cut short where
// the run stopped, so the lengths are the shots before it, and the figures say they are of the part read.
function fill(seconds, read, stopped) {
  const total = ends.at(-1) - starts[0], whole = stopped ? shots.slice(0, -1) : shots;
  const lengths = whole.map(s => s.durationSeconds);
  const n = lengths.length, cuts = shots.length - 1;
  const sorted = [...lengths].sort((a, b) => a - b);
  const median = n % 2 === 1 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
  const shortest = lengths.indexOf(sorted[0]), longest = lengths.indexOf(sorted.at(-1));
  const factor = read / seconds, part = stopped ? ' of the part read' : '';
  const finished = stopped ? `the ${n} ${n === 1 ? 'shot' : 'shots'} before the last, which the stop cut short` : `${n} shots`;
  for (const [term, value, title] of [
    ['Shots', `${shots.length}`, `${shots.length} ${shots.length === 1 ? 'shot' : 'shots'} in ${total.toFixed(3)} s` +
      `${stopped ? ', the last cut short where the run stopped' : ''}`],
    ...n === 0 ? [['Lengths', 'none finished', 'The run stopped inside its first shot: no shot has its whole length yet']] : [
      ['Average length', length(lengths.reduce((a, b) => a + b, 0) / n), `Over ${finished}`],
      ['Median length', length(median), `${median.toFixed(6)} s: half of ${finished} are shorter, half longer`],
      ...n === 1 ? [['Length', which(0), `${finished}: shot ${whole[0].shot}, ${lengths[0].toFixed(3)} s. Play it`]] : [
        ['Shortest', which(shortest), `Shot ${whole[shortest].shot}, ${lengths[shortest].toFixed(3)} s. Play it`],
        ['Longest', which(longest), `Shot ${whole[longest].shot}, ${lengths[longest].toFixed(3)} s. Play it`]]],
    ['Cuts per minute', total > 0 && (cuts > 0 || !stopped) ? (cuts / total * 60).toFixed(1) : '–',
      `${cuts} ${cuts === 1 ? 'cut' : 'cuts'} in ${(total / 60).toFixed(3)} min${part}` +
      `${cuts === 0 && stopped ? ': none yet, too little read for a rate' : ''}`],
    ...seconds === null ? [] : [['Run', `${factor.toFixed(1)} times real time`,
      `${seconds.toFixed(1)} s for ${read.toFixed(1)} s of video${read < total - 1e-6 ? ' (the rest of a stopped run, ' +
        'from the keyframe it scored again from)' : ''}${factor >= 1 ? '' : ': slower than playing it'}`]],
  ]) {
    const dt = document.createElement('dt'), dd = document.createElement('dd');
    dt.textContent = term;
    if (typeof value === 'string') dd.textContent = value; else dd.append(value);
    dd.title = title;
    list.append(dt, dd);
  }
}

const length = s => `${s.toFixed(2)} s`;

// A shot's length and number, a button that plays it.
function which(i) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'shot-link';
  b.textContent = `${length(shots[i].durationSeconds)} (shot ${shots[i].shot})`;
  b.onclick = () => seek(starts[i], { play: true });
  return b;
}
