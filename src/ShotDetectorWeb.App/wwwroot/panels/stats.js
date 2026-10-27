// The shots' statistics, a card (backlog "Shot statistics and a timeline"): from the shots as Detect returns them, how
// many, their average and median length, the shortest and the longest (a click plays it), cuts per minute and how long
// the run took against the length it read. They are the page's own figures, worked out here from the shots, not
// ShotDetector's, and the card's mark says so. They wait for a finished run. The shot playing is app.js's caption under
// the video, and the shots table marks its row.
import { card, on } from '../app.js';
import { secondsOf, seek } from '../timeline.js';

const section = card('Shot statistics', {
  mark: 'page',
  markTitle: 'Worked out by the page from the shots ShotDetector found, not ShotDetector\'s numbers',
});
section.id = 'stats';
section.hidden = true;
const list = document.createElement('dl');
const note = document.createElement('p');
note.className = 'note';
note.textContent = 'From the run\'s shots: their lengths as in the scenes CSV, a cut where each shot after the first ' +
  'starts, the length read from the first shot\'s start to the last one\'s end. Exact values in the tooltips.';
section.append(list, note);

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
on('done', ({ result, seconds }) => {
  clear();
  for (const shot of result.shots) add(shot);
  if (shots.length === 0) return;
  fill(seconds);
  section.hidden = false;
});

function fill(seconds) {
  const lengths = shots.map(s => s.durationSeconds);
  const n = lengths.length, total = ends.at(-1) - starts[0];
  const sorted = [...lengths].sort((a, b) => a - b);
  const median = n % 2 === 1 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
  const shortest = lengths.indexOf(sorted[0]), longest = lengths.indexOf(sorted.at(-1));
  const cuts = n - 1;
  const factor = total / seconds;
  for (const [term, value, title] of [
    ['Shots', `${n}`, `${n} ${n === 1 ? 'shot' : 'shots'} in ${total.toFixed(3)} s`],
    ['Average length', length(total / n), `${(total / n).toFixed(6)} s: ${total.toFixed(3)} s over ${n} shots`],
    ['Median length', length(median), `${median.toFixed(6)} s: half the shots are shorter, half longer`],
    ['Shortest', which(shortest), `Shot ${shots[shortest].shot}, ${lengths[shortest].toFixed(3)} s. Play it`],
    ['Longest', which(longest), `Shot ${shots[longest].shot}, ${lengths[longest].toFixed(3)} s. Play it`],
    ['Cuts per minute', total > 0 ? (cuts / total * 60).toFixed(1) : '–',
      `${cuts} ${cuts === 1 ? 'cut' : 'cuts'} in ${(total / 60).toFixed(3)} min`],
    ['Run', `${factor.toFixed(1)} times real time`,
      `${seconds.toFixed(1)} s for ${total.toFixed(1)} s of video${factor >= 1 ? '' : ': slower than playing it'}`],
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
