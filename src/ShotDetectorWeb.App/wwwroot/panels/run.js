// The run, a card beside the video: its figures large (how many shots so far while it goes, then the shots, the cuts,
// the length read and the options it ran with), and the downloads, the scene and stats CSVs as scenedetect writes them
// and the shots as ShotDetector's JSON and scenedetect's HTML page (Api's ShotsJson and ShotsHtml, asked for here once
// the run is done), named after the file; for editors, the shots in scenedetect's timeline formats (Api's TimelineFile:
// an EDL, FCPXML, FCP7 XML, OTIO, an x264 QP file and Aegisub keyframes), which name the file at the root, as the
// browser doesn't know its folder: the editor relinks it, and Subtitle Edit's shot changes; and YouTube chapters (Api's
// YouTubeChapters: ShotDetector's Timeline's rule and lines), the cuts as a video description's chapter list, each at
// least the minimum typed beside it (30 s to begin with, YouTube's 10 at least), with a title for each typed on the page
// (Titles, folded under the row: the download carries them, a chapter left empty keeps Timeline's "Chapter n"; kept
// while the run is shown, by the chapter's start, so a new minimum keeps the titles of the starts it keeps). A stopped run's are the result for the part
// read, and its figures say where it stopped. A run lost with its worker (one that didn't answer a stop) says where it
// had got to and has no downloads (.NET and the result went with the worker).
import { call, card, frameSeconds, on } from '../app.js';
import { secondsOf, timecode } from '../timeline.js';

const section = card('This run', {
  place: 'run',
  mark: 'exact',
  markTitle: 'The shots, the CSVs, the JSON, the HTML and the editors\' files are ShotDetector\'s, the CSVs, the HTML ' +
    'and the editors\' files written as scenedetect writes them: the checks compare them byte for byte with ' +
    'ShotDetector\'s FFmpeg path on every sample, natively and on WebAssembly',
});
section.id = 'run';
section.insertAdjacentHTML('beforeend', `
  <dl class="figures"></dl>
  <p id="summary"></p>
  <p class="downloads" hidden>
    <a id="scenes-csv" download>Scenes CSV</a>
    <a id="stats-csv" download>Stats CSV</a>
    <a id="shots-json" download>Shots JSON</a>
    <a id="shots-html" download>Shots HTML</a>
  </p>
  <p class="downloads editors" hidden>
    <span>For editors:</span>
    <a id="timeline-edl" download>EDL</a>
    <a id="timeline-fcpxml" download>FCPXML</a>
    <a id="timeline-fcp7" download>FCP7 XML</a>
    <a id="timeline-otio" download>OTIO</a>
    <a id="timeline-qp" download>QP</a>
    <a id="timeline-keyframes" download>Keyframes</a>
    <a id="timeline-shotchanges" download>Shot changes</a>
  </p>
  <p class="downloads chapters" hidden>
    <span>YouTube chapters of at least</span>
    <label><input id="chapters-min" type="number" min="10" step="1" value="30" aria-label="Shortest chapter, in seconds"> s:</label>
    <a id="chapters-txt" download>Chapters</a>
    <span id="chapters-none"></span>
  </p>
  <details class="chapter-titles" hidden>
    <summary>Titles</summary>
    <ol></ol>
  </details>`);
const figures = section.querySelector('.figures'), summary = section.querySelector('#summary');
const downloads = section.querySelector('.downloads'), editors = section.querySelector('.editors');
const chapters = section.querySelector('.chapters'), chaptersMin = section.querySelector('#chapters-min');
const chaptersLink = section.querySelector('#chapters-txt'), chaptersNone = section.querySelector('#chapters-none');
const titles = section.querySelector('.chapter-titles'), titleList = titles.querySelector('ol');
// The chapters row's tooltip: whose they are and how they are made (ShotDetector's Timeline.YouTubeChapters).
chapters.title = 'ShotDetector\'s, not scenedetect\'s: the cuts as the chapter list of a YouTube description. Shots ' +
  'merge until a chapter has run the minimum, each cut\'s time rounded up to the second; a last chapter shorter than ' +
  'that joins the one before. YouTube wants the first at 0:00, at least three, and each at least 10 s. Type the ' +
  'titles under Titles.';
const links = ['#scenes-csv', '#stats-csv', '#shots-json', '#shots-html'].map(id => section.querySelector(id));
// The editors' files: Api's TimelineFile format, the link, the file's extension, its type and what reads it.
const TIMELINES = [
  ['edl', 'timeline-edl', 'edl', 'text/plain', 'a CMX 3600 edit decision list, a cut a shot (DaVinci Resolve, Avid and ' +
    'most editors import it as a timeline; its events name reel AX, not the file)'],
  ['fcpx', 'timeline-fcpxml', 'fcpxml', 'application/xml', 'Final Cut Pro X XML (FCPXML 1.9), a clip a shot (Final Cut ' +
    'Pro, DaVinci Resolve)'],
  ['fcp7', 'timeline-fcp7', 'fcp7.xml', 'application/xml', 'Final Cut Pro 7 XML (xmeml), a clip a shot (Premiere Pro, ' +
    'DaVinci Resolve)'],
  ['otio', 'timeline-otio', 'otio', 'application/json', 'an OpenTimelineIO timeline, a clip a shot (DaVinci Resolve ' +
    '18.5 and later, OpenTimelineIO\'s tools)'],
  ['qp', 'timeline-qp', 'qp', 'text/plain', 'an x264/x265 QP file, a keyframe at each cut, to encode the video so each ' +
    'shot starts on one'],
  ['keyframes', 'timeline-keyframes', 'keyframes.txt', 'text/plain', 'Aegisub\'s keyframes (format v1), frame 0 and each ' +
    'cut, for timing subtitles to the cuts'],
  ['shotchanges', 'timeline-shotchanges', 'shotchanges', 'text/plain', 'Subtitle Edit\'s shot changes, each cut\'s time ' +
    'in seconds (its Import shot changes; ShotDetector\'s, not scenedetect\'s)'],
].map(([format, id, extension, type, what]) => ({ format, link: section.querySelector(`#${id}`), extension, type, what }));
const RELINK = 'It names the file at the root, as the browser doesn\'t know its folder: relink it in the editor.';
let shots = [];
let options = null; // the run's, as Api's Detect takes them (null for a default)
let runs = 0; // counts the runs and files chosen, so a result asked for after one is done isn't shown under the next
let fileName = ''; // the file chosen's, which the editors' files name
let chaptered = null; // the run whose chapters are shown: { at, name, part, minimum, text }, minimum the one shown
const typed = new Map(); // the titles typed for the run shown, by the chapter's start in seconds

const count = n => `${n} ${n === 1 ? 'shot' : 'shots'}`;

// The figures: each a value, what it is under it, a tooltip, and a class (the detector's: smaller than the numbers).
function show(...figs) {
  figures.replaceChildren(...figs.map(([value, term, title = '', kind = '']) => {
    const group = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd');
    if (kind) group.className = kind;
    dt.textContent = term;
    dd.textContent = value;
    group.title = title;
    group.append(dt, dd);
    return group;
  }));
}

// The detector, and its threshold and minimum shot length, as the run was told them.
function ran() {
  const { detector, threshold, minSceneLength } = options;
  return [detector, `${threshold == null ? 'default threshold' : `threshold ${threshold}`} · ` +
    `${minSceneLength == null ? 'default minimum shot length' : `minimum shot length ${minSceneLength}`}`,
    'The detector, threshold and minimum shot length the run used; a default is ShotDetector\'s own', 'detector'];
}

function clear() {
  for (const a of [...links, ...TIMELINES.map(t => t.link)]) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
    a.hidden = false;
  }
  downloads.hidden = editors.hidden = chapters.hidden = true;
  if (chaptersLink.href) URL.revokeObjectURL(chaptersLink.href);
  chaptersLink.removeAttribute('href');
  chaptersNone.textContent = '';
  titles.hidden = true;
  titleList.replaceChildren();
  typed.clear();
  figures.replaceChildren();
  summary.textContent = '';
  shots = [];
}

on('chose', ({ file }) => {
  runs++;
  fileName = file.name;
  clear();
  options = null;
});
on('run', ({ options: o, ranges, from }) => {
  runs++;
  clear();
  options = o;
  show(['0', 'shots so far'], ran());
  // A run split across workers decides its shots when their ranges merge (decision 2026-10-26-workers.md), but for the
  // first range's, from frame 0, which come as they are found (a resumed run's rest has no such range).
  summary.textContent = !ranges ? 'The shots appear as they are found.'
    : from === 0 ? `The first worker's shots appear as they are found, the rest once the ${ranges.length} workers' ` +
      'ranges merge.'
    : `The shots come once the ${ranges.length} workers' ranges merge.`;
});
on('shot', ({ shot }) => {
  // A shot told again (a resumed run's last, or a split run's first range's last when the ranges merge) replaces the
  // ones from its number on.
  shots.length = Math.min(shots.length, shot.shot - 1);
  shots.push(shot);
  show([String(shots.length), `${shots.length === 1 ? 'shot' : 'shots'} so far`], ran());
});
on('done', async ({ video, result: r, name }) => {
  clear();
  const rate = r.frameRate.num / r.frameRate.den, n = r.shots.length, cuts = Math.max(n - 1, 0);
  const fps = `${Number(rate.toFixed(3))} fps${video.frameTimestamps ? ' on average' : ''}`;
  if (r.stopped) {
    show([String(n), `${n === 1 ? 'shot' : 'shots'}, the last ending where the run stopped`],
      [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [timecode(frameSeconds(video, r.frameCount)), `stopped at: frame ${r.frameCount.toLocaleString('en')} of ` +
        `${video.frames.toLocaleString('en')} read`, `Stopped at frame ${r.frameCount} of ${video.frames}, at ${fps}`],
      ran());
    summary.textContent = `${count(n)} in the ${r.frameCount} frames read. The CSVs are the ones for that part of the video.`;
  } else {
    show([String(n), n === 1 ? 'shot' : 'shots'], [String(cuts), cuts === 1 ? 'cut' : 'cuts'],
      [r.duration != null ? `${r.duration.toFixed(2)} s` : `${r.frameCount} frames`,
        `${r.frameCount.toLocaleString('en')} frames at ${fps}`, `${count(n)} in ${r.frameCount} frames at ${fps}` +
        `${r.duration != null ? `, ${r.duration} s` : ''}`],
      ran());
  }
  // The shots as JSON and HTML, of the run just done (the worker keeps its result until the next run).
  const at = runs, json = await call('ShotsJson'), html = await call('ShotsHtml');
  if (at !== runs) return;
  const [scenes, stats, shotsJson, shotsHtml] = links;
  const part = r.stopped ? ', for the part read' : '';
  link(scenes, r.scenesCsv, `${name}.scenes.csv`, 'text/csv', `The shots, as scenedetect writes them${part}`);
  link(stats, r.statsCsv, `${name}.stats.csv`, 'text/csv', `Each frame's scores, as scenedetect writes them${part}`);
  link(shotsJson, json, `${name}.shots.json`, 'application/json', `The shots as ShotDetector's JSON${part}`);
  link(shotsHtml, html, `${name}.shots.html`, 'text/html', `The shots as scenedetect's HTML page${part}`);
  downloads.hidden = false;
  // The editors' files, of the same run. One that can't be written (FCPXML and FCP7 XML of no shots) isn't offered.
  const files = await Promise.all(TIMELINES.map(t => call('TimelineFile', t.format, fileName).catch(() => null)));
  if (at !== runs) return;
  TIMELINES.forEach((t, i) => {
    t.link.hidden = files[i] === null;
    if (files[i] === null) return;
    link(t.link, files[i], `${name}.${t.extension}`, t.type, `The shots as ${t.what}` +
      `${t.format === 'shotchanges' ? '' : ', as scenedetect writes it'}${part}.` +
      `${['fcpx', 'fcp7', 'otio'].includes(t.format) ? ` ${RELINK}` : ''}`);
  });
  editors.hidden = files.every(f => f === null);
  chaptered = { at, name, part };
  await showChapters();
});

// The chapters of the run done, at the minimum typed (a whole number of seconds, 10 at least), again as it is typed;
// a minimum already shown is left as it is (typing "15" asks for 10, then 15; deleting back to "1" asks nothing).
const typedMinimum = () => Math.max(10, Math.round(Number(chaptersMin.value) || 30));
async function showChapters() {
  if (chaptered === null || chaptered.at !== runs) return;
  const { at, name, part } = chaptered;
  const minimum = typedMinimum();
  if (chaptered.minimum === minimum) return;
  chaptered.minimum = minimum;
  const c = JSON.parse(await call('YouTubeChapters', minimum));
  if (at !== runs || chaptered.minimum !== minimum) return;
  if (chaptersLink.href) URL.revokeObjectURL(chaptersLink.href);
  chaptersLink.removeAttribute('href');
  chaptersLink.hidden = c.text === null;
  chaptersNone.textContent = c.notMade ?? '';
  chaptered.text = c.text;
  chaptered.starts = c.starts;
  showTitles();
  chapters.hidden = false;
}
chaptersMin.addEventListener('input', showChapters);

// Timeline's lines with the titles typed: each line is its start ("1:23"), a space and Timeline's placeholder title,
// which a title typed for that start replaces.
function titled() {
  const { text, starts } = chaptered;
  return text.split('\n').map((line, i) => {
    const title = typed.get(starts[i])?.trim();
    return i < starts.length && title ? `${line.slice(0, line.indexOf(' '))} ${title.replace(/\s+/g, ' ')}` : line;
  }).join('\n');
}
function linkChapters() {
  const { name, part, minimum, starts } = chaptered;
  if (chaptersLink.href) URL.revokeObjectURL(chaptersLink.href);
  const named = starts.filter(s => typed.get(s)?.trim()).length;
  link(chaptersLink, titled(), `${name}.chapters.txt`, 'text/plain', `${starts.length} chapters of at least ` +
    `${minimum} s, the lines to paste into the video's description${part}` +
    `${named > 0 ? `, ${named} titled on the page` : ''}`);
}
// A field per chapter, its start beside it, Timeline's title its placeholder.
function showTitles() {
  titleList.replaceChildren();
  titles.hidden = chaptered.text === null;
  if (chaptered.text === null) return;
  const lines = chaptered.text.split('\n');
  chaptered.starts.forEach((start, i) => {
    const li = document.createElement('li'), time = document.createElement('span'), input = document.createElement('input');
    const line = lines[i], space = line.indexOf(' ');
    time.textContent = line.slice(0, space);
    input.type = 'text';
    input.placeholder = line.slice(space + 1);
    input.value = typed.get(start) ?? '';
    input.setAttribute('aria-label', `Title of the chapter at ${time.textContent}`);
    input.addEventListener('input', () => {
      typed.set(start, input.value);
      linkChapters();
    });
    li.append(time, input);
    titleList.append(li);
  });
  linkChapters();
}
on('cancelled', ({ video, done, expected }) => {
  const from = shots.length > 0 ? secondsOf(shots.at(-1).endTimecode) : 0, to = frameSeconds(video, done);
  show([String(shots.length), `${shots.length === 1 ? 'shot' : 'shots'} found`],
    [timecode(to), `lost at: frame ${done.toLocaleString('en')}${expected > 0 ? ` of ${expected.toLocaleString('en')}` : ''}`],
    ran());
  summary.textContent = `Lost at frame ${done}${expected > 0 ? ` of ${expected}` : ''}: ` +
    `${count(shots.length)} found${to > from ? ', then one unfinished up to where the run had got to' : ''}. ` +
    'The worker didn\'t answer the stop, so there are no downloads.';
});

function link(a, text, name, type, title) {
  a.href = URL.createObjectURL(new Blob([text], { type }));
  a.download = name;
  a.title = `${name}: ${title}`;
}
