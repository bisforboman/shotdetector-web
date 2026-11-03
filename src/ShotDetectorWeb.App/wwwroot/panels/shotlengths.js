// Shot lengths for film studies (issue #91; the owner's choice, 2026-11-02: cards, not a mode), a card under the
// timeline, a row of its own under the shots and their statistics: the run's shots (the corrected ones once the cuts
// are corrected on the page, 'corrected') as a bar a shot along the film, as Cinemetrics draws them: each bar where its
// shot starts, its width the shot's time on screen, its height the shot's length, with a trend line through them (a
// least-squares polynomial of degree 6, fewer for few shots, over the shots' middles). A click on a bar plays the shot.
// Under it, a CSV of the shot lengths to download: a row a shot, its number, first frame and start, its length in
// frames and seconds, the numbers of the scenes CSV's shots (1-based frames), the plain list that spreadsheets and
// shot-length tools read. Everything here is the page's own reading of the shots, and the card's mark says so. A
// stopped run's last shot, cut short at the stop, has no length here.
import { card, on } from '../app.js';
import { secondsOf, seek, timecode } from '../timeline.js';

const DEGREE = 6;
const W = 600, H = 160; // the SVG's own units, stretched to the card's width

const section = card('Shot lengths', {
  place: 'below',
  mark: 'page',
  markTitle: 'Worked out by the page from the shots ShotDetector found (or as you corrected them): a bar a shot, its ' +
    'height its length, a least-squares trend line through them, and the lengths as a CSV. Not ShotDetector\'s numbers',
});
section.id = 'shot-lengths';
section.hidden = true;
section.insertAdjacentHTML('beforeend', `
  <svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img" aria-label="Each shot's length along the film">
    <g class="bars"></g><path class="trend" fill="none" vector-effect="non-scaling-stroke"></path>
  </svg>
  <p class="ends"><span class="from">00:00:00.000</span><span class="top"></span><span class="to"></span></p>
  <p class="downloads"><a id="shot-lengths-csv" download>Shot lengths CSV</a> <span class="note"></span></p>`);
const svg = section.querySelector('svg'), bars = svg.querySelector('.bars'), trend = svg.querySelector('.trend');
const scale = section.querySelector('.top'), to = section.querySelector('.to'), note = section.querySelector('.note');
const link = section.querySelector('#shot-lengths-csv');

let name = '';  // the file's name without its extension, for the download
let ran = null; // the run done's { stopped }

function clear() {
  section.hidden = true;
  bars.replaceChildren();
  trend.removeAttribute('d');
  if (link.href) URL.revokeObjectURL(link.href);
  link.removeAttribute('href');
}

on('chose', () => { ran = null; clear(); });
on('run', () => { ran = null; clear(); });
on('done', ({ result, name: n }) => {
  name = n;
  ran = { stopped: result.stopped };
  draw(result.shots, false);
});
on('corrected', ({ result, edits }) => {
  if (ran) draw(result.shots, edits.removed.length + edits.added.length > 0);
});

function draw(all, corrected) {
  clear();
  // A stopped run's last shot is cut short at the stop: not a length.
  const shots = (ran.stopped ? all.slice(0, -1) : all).map(s => ({
    shot: s.shot, frame: s.startFrame, frames: s.durationFrames, start: secondsOf(s.startTimecode),
    length: s.durationSeconds,
  }));
  if (shots.length === 0) return;
  const end = shots.at(-1).start + shots.at(-1).length, longest = Math.max(...shots.map(s => s.length));
  const sx = t => t / end * W, sy = l => H - l / longest * H;
  for (const s of shots) {
    const r = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    r.setAttribute('x', sx(s.start));
    r.setAttribute('width', Math.max(sx(s.length), 0.5));
    r.setAttribute('y', sy(s.length));
    r.setAttribute('height', H - sy(s.length));
    const title = document.createElementNS('http://www.w3.org/2000/svg', 'title');
    title.textContent = `Shot ${s.shot}: ${s.length.toFixed(3)} s, from ${timecode(s.start)}. Play it`;
    r.append(title);
    r.addEventListener('click', () => seek(s.start, { play: true }));
    bars.append(r);
  }
  const fit = polynomial(shots.map(s => (s.start + s.length / 2) / end), shots.map(s => s.length));
  if (fit) {
    const points = Array.from({ length: 121 }, (_, i) => i / 120);
    trend.setAttribute('d', points.map((x, i) =>
      `${i === 0 ? 'M' : 'L'}${(x * W).toFixed(1)},${Math.min(Math.max(sy(fit.at(x)), 0), H).toFixed(1)}`).join(''));
  }
  svg.setAttribute('aria-label', `${shots.length} shots' lengths along ${timecode(end)}, the longest ${longest.toFixed(2)} s`);
  scale.textContent = `bars up to ${longest.toFixed(2)} s${fit ? `, trend line of degree ${fit.degree}` : ''}`;
  to.textContent = timecode(end);
  const csv = ['Shot,Start Frame,Start Time (seconds),Length (frames),Length (seconds)',
    ...shots.map(s => `${s.shot},${s.frame},${s.start.toFixed(3)},${s.frames},${s.length.toFixed(3)}`)].join('\n') + '\n';
  link.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
  link.download = `${name}.shot-lengths.csv`;
  link.title = `${name}.shot-lengths.csv: a row a shot, its number, first frame (1-based, as the scenes CSV), start, ` +
    `and length in frames and seconds${corrected ? ', of the cuts as corrected on the page' : ''}` +
    `${ran.stopped ? '; the stopped run\'s shots before the last, which the stop cut short' : ''}. The page's, not ShotDetector's`;
  note.textContent = `${shots.length} ${shots.length === 1 ? 'shot' : 'shots'}${corrected ? ', as corrected' : ''}`;
  section.hidden = false;
}

// The least-squares polynomial through (x, y), x in [0, 1], of degree 6 or fewer (as many as the points allow less
// one, at most a third of them): { degree, at(x) }, or null for fewer than 3 points. Over x mapped to [-1, 1], the
// normal equations solved by Gaussian elimination with partial pivoting.
function polynomial(xs, ys) {
  const degree = Math.min(DEGREE, xs.length - 1, Math.floor(xs.length / 3));
  if (degree < 1) return null;
  const n = degree + 1, a = Array.from({ length: n }, () => new Array(n + 1).fill(0));
  xs.forEach((x0, k) => {
    const x = 2 * x0 - 1, powers = [1];
    for (let i = 1; i <= 2 * degree; i++) powers.push(powers[i - 1] * x);
    for (let i = 0; i < n; i++) {
      for (let j = 0; j < n; j++) a[i][j] += powers[i + j];
      a[i][n] += powers[i] * ys[k];
    }
  });
  for (let c = 0; c < n; c++) {
    let pivot = c;
    for (let r = c + 1; r < n; r++) if (Math.abs(a[r][c]) > Math.abs(a[pivot][c])) pivot = r;
    [a[c], a[pivot]] = [a[pivot], a[c]];
    if (Math.abs(a[c][c]) < 1e-12) return null;
    for (let r = 0; r < n; r++) {
      if (r === c) continue;
      const f = a[r][c] / a[c][c];
      for (let j = c; j <= n; j++) a[r][j] -= f * a[c][j];
    }
  }
  const coefficients = a.map((row, i) => row[n] / row[i]);
  return {
    degree,
    at: x0 => {
      const x = 2 * x0 - 1;
      return coefficients.reduceRight((sum, c) => sum * x + c, 0);
    },
  };
}
