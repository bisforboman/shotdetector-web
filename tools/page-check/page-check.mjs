// The page's smoke test (README "Checks", page; decision 2026-10-24-the-page.md, "Testing the page"): the published
// wwwroot served as Pages serves it, with the demo clip in demo/ (pages.yml's "Make the site"), and the page itself in
// Chromium through Playwright, its own JavaScript (app.js, worker.js, the panels) as a visitor drives it:
//   0. every page first becomes cross-origin isolated: its service worker (sw.js) takes over and the page reloads once,
//      so one-worker runs go on the threaded runtime on the page's own thread (threads.js; decision
//      2026-10-30-threads.md), as on Pages;
//   1. a sample set on the file input, detected with the options as the page prefills them, on threads: its scenes and
//      stats CSVs, its shots as JSON and HTML and the editors' timeline files (EDL, FCPXML, FCP7 XML, OTIO, QP, keyframes)
//      downloaded from the run's card and compared with the native check's run of that detector byte for byte (the
//      timeline files but for the file's name: the page gives the picked file's), its YouTube chapters at 30 s and 10 s
//      the native run's, the shots table one row per shot of the CSV;
//   2. the same sample on the page's worker (?threads=0, as where the page isn't isolated), and split across two
//      workers (?workers=2): the ranges lane; the same downloads each time;
//   3. "Try the demo": the clip fetched and run as picked, on threads, paused a quarter of the way in (the bar and the
//      status held still, the button reading Resume), resumed, the same comparison, the sound measured;
//   4. the demo detected again, paused and stopped while paused: it ends stopped at the pause it was held at, its
//      stats CSV the native run's first rows up to there, the shots table, the JSON and the HTML a shot each of its
//      scenes CSV's;
//   5. across a reload (backlog "Resume after Stop, across visits"): the page reloaded, the demo picked again shows the
//      stopped run kept in IndexedDB (its downloads the stopped run's, its options in the form, Resume beside Detect),
//      and Resume goes on from there on threads to the whole run's downloads;
//   6. offline: the page reloaded with the network off comes from the service worker's cache, isolated, and the demo
//      (cached when it was fetched) runs on threads to the same downloads;
//   7. the demo split across two workers (?workers=2), stopped half-way and resumed in the same visit: the rest split
//      again, merged with the kept scores to the whole run's downloads;
//   8. an AVI (MJPEG, which no browser plays) picked on threads: the player the file's own JPEG at the playhead (an
//      <img> in the video's place), the frame the playhead is in after a click on the timeline and on a shot's row,
//      decoded; a still per shot from the same JPEGs; the downloads the native run's (backlog "The picture of an AVI on
//      the page");
//   9. in Firefox, on the page's worker (?threads=0): the demo stopped, the page reloaded, the demo picked again shown
//      stopped and resumed to the whole run's downloads (PAGE_CHECK_FIREFOX=skip leaves it out where Playwright has no
//      Firefox: a cloud session's).
//  10. each more sample (a rotated video, a variable frame rate, ...: what the first sample doesn't have) picked on
//      threads and split across two workers, after 2: the same downloads, picture and frame types as 1 and 2; a
//      variable-rate video's second range drawn at its first frame's own time. Then the --deinterlace sample with
//      Deinterlace auto on threads, and asked for two workers: on one, the ranges lane saying why; the downloads the
//      native check's deinterlace-auto run's.
// The demo's whole run, the stopped run shown again after the reload and its resume are each checked for the layout
// (looks: no hole in the details, the shots unscrolled, no red in a lane but the playhead, the run's figures in the
// first screen) and shot at 1280 and 390 px, light and dark, into <out>.
// After each whole run (1 to 3, 10), the picture's card (panels/picture.js) shows the native check's cropdetect line and
// colour bars, and its lane is there; the frame types' card (panels/frametypes.js) the counts of ffprobe's pict_type
// as the native check wrote them, and its lane is there.
// Any error on the page, in its workers or a failed request fails the check.
//
// Usage: node tools/page-check/page-check.mjs <wwwroot> <out> <sample.mp4> <demo.mp4> <sample.avi> --native <out/native> [<more.mp4>...]
//   Playwright from tools/page-check/node_modules (npm ci there; its Chromium: npx playwright install chromium), or
//   else wherever require finds it (a cloud session's pre-installed one). <out> gets the downloaded files.
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, extname, join, normalize, resolve } from 'node:path';

const args = process.argv.slice(2);
const at = args.indexOf('--native'), di = args.indexOf('--deinterlace');
if (at < 0 || args.length < 7 || (di >= 0 && di + 1 >= args.length)) {
  console.error('Usage: node page-check.mjs <wwwroot> <out> <sample.mp4> <demo.mp4> <sample.avi> --native <out/native> ' +
    '[--deinterlace <interlaced.mp4>] [<more.mp4>...]');
  process.exit(2);
}
const nativeDir = resolve(args[at + 1]);
const interlaced = di >= 0 ? resolve(args[di + 1]) : null;
const [root, outDir, sample, demo, avi, ...more] = args.filter((_, i) => ![at, at + 1, di, di + 1].includes(i)).map(a => resolve(a));
// The file each run's name (as the page saw it) is, for the native check's files of it.
const FILES = { 'sintel-trailer.mp4': demo, [basename(sample)]: sample, [basename(avi)]: avi,
  ...Object.fromEntries([...more, ...(interlaced ? [interlaced] : [])].map(m => [basename(m), m])) };
const stem = file => basename(file, extname(file));
mkdirSync(outDir, { recursive: true });

let playwright;
try {
  playwright = await import('playwright');
} catch {
  playwright = createRequire(import.meta.url)('playwright');
}
const { chromium, firefox } = playwright;

// Pages' types for what the bundle has; anything else as bytes. .wasm must be application/wasm (README, "Running the
// page").
const TYPES = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.wasm': 'application/wasm', '.mp4': 'video/mp4', '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon', '.png': 'image/png', '.txt': 'text/plain',
};
const server = createServer((req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  const file = path === '/demo/sintel-trailer.mp4' ? demo
    : join(root, normalize(path === '/' ? '/index.html' : path));
  if (!file.startsWith(root) && file !== demo || !existsSync(file) || !statSync(file).isFile()) {
    res.writeHead(404).end();
    return;
  }
  res.writeHead(200, {
    'Content-Type': TYPES[extname(file)] ?? 'application/octet-stream',
    'Content-Length': statSync(file).size,
  });
  createReadStream(file).pipe(res);
});
await new Promise(ok => server.listen(0, '127.0.0.1', ok));
const base = `http://127.0.0.1:${server.address().port}/`;

const browser = await chromium.launch();
const failures = [];
const fail = message => { failures.push(message); console.log(`FAIL ${message}`); };

// Each page's switch for the leave-out below, for a reload the check makes itself (resumedAfterReload).
const settlings = new WeakMap();

// A fresh page each time, as a visitor's first visit (no rate kept from an earlier run), its errors collected:
// exceptions, console errors (the workers' too) and failed or missing requests.
async function open(query = '', on = browser) {
  const context = await on.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  const errors = [];
  // A first visit reloads once its service worker takes over (app.js, serve), which aborts the replaced document's
  // runtime as it loads: Firefox reports that ("error loading dynamically imported module", requests
  // NS_BINDING_ABORTED; CI, 2026-10-30), Chromium doesn't. That isn't the page's error, and the reloaded document loads
  // it all again under watch: until the page has settled, a request aborted, what a replaced document or its worker
  // says, and the aborted import itself (Firefox's page reports its worker's too, after the reload) are left out. A
  // runtime that really doesn't load fails the runs that follow. Each document counts as one; its workers belong to it.
  let documents = 0, settling = true;
  settlings.set(page, on => { settling = on; });
  const aborted = /error loading dynamically imported module/;
  const counted = (doc, text) => { if (!(settling && (doc < documents || aborted.test(text)))) errors.push(text); };
  page.on('framenavigated', f => { if (f === page.mainFrame()) documents++; });
  const watch = (where, target) => {
    target.on('console', m => { if (m.type() === 'error') counted(documents, `${where} console: ${m.text()}`); });
    target.on('pageerror', e => counted(documents, `${where}: ${e.message}`));
  };
  watch('page', page);
  page.on('worker', w => {
    const doc = documents;
    w.on('console', m => { if (m.type() === 'error') counted(doc, `worker console: ${m.text()}`); });
  });
  // The <video> aborts its own range requests of the file's blob URL as it seeks and buffers: not an error.
  page.on('requestfailed', r => {
    const why = r.failure()?.errorText;
    if (r.resourceType() === 'media' && why === 'net::ERR_ABORTED') return;
    if (settling && /ABORTED/.test(why ?? '')) return;
    errors.push(`request failed: ${r.url()} (${why})`);
  });
  page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()}: ${r.url()}`); });
  await page.goto(base + query);
  await (on === browser ? isolated(page) : served(page));
  settling = false;
  return { context, page, errors };
}

// Another browser, which needn't become isolated (the runs go on the page's worker): its service worker in control,
// and the reload a first visit makes, if it makes one (app.js, serve), done.
async function served(page) {
  await page.waitForFunction(() => navigator.serviceWorker?.controller != null, null, { timeout: 30000 });
  await page.waitForTimeout(2000);
  await page.waitForLoadState('load');
}

// Stops a run once its bar reaches at (a fraction): the stopped result, its frames read (null if it didn't stop).
async function stopped(page, what, at) {
  await page.waitForFunction(f => document.body.dataset.state === 'detecting' &&
    document.getElementById('progress').value >= f, at, { timeout: 15 * 60 * 1000, polling: 50 });
  await page.click('#stop');
  const status = await finished(page, what, 'stopped');
  return status === null ? null : Number(/frame ([\d,]+) of/.exec(status)?.[1].replace(/,/g, ''));
}

// The page reloaded (a later visit) and the demo picked again: the stopped run kept in this browser shown at once,
// stopped at frames, with Resume and Forget, its downloads the stopped run's; then resumed to the whole run's.
async function resumedAfterReload(page, what, frames, run, settle, threads) {
  settlings.get(page)(true);
  await page.reload();
  await settle(page);
  settlings.get(page)(false);
  await page.click('#demo');
  const status = await finished(page, `${what}, shown again`, 'stopped');
  if (status === null) return;
  const at = Number(/frame ([\d,]+) of/.exec(status)?.[1].replace(/,/g, ''));
  if (at !== frames || !status.includes('kept in this browser') || !await page.isVisible('#resume') ||
    await page.inputValue('#detector') !== run)
    fail(`${what}: after the reload, not the stopped run at frame ${frames} kept with Resume: ${status}`);
  await compare(page, `${what}, shown again`, 'sintel-trailer.mp4', run, frames);
  await keptCards(page, `${what}, shown again`, frames);
  await looks(page, `${what}, shown again`, `${what.replace(/\W+/g, '-')}.kept`);
  await page.click('#resume');
  const done = await finished(page, `${what}, resumed`);
  if (done === null) return;
  if (!resumedAt(done, frames) || THREADED.test(done) !== threads) fail(`${what}: resumed, the status says ${done}`);
  await realTime(page, `${what}, resumed`, done);
  await compare(page, `${what}, resumed`, 'sintel-trailer.mp4', run);
  await looks(page, `${what}, resumed`, `${what.replace(/\W+/g, '-')}.resumed`);
  if (await page.isVisible('#resume')) fail(`${what}: finished, Resume is still offered`);
}

// A resumed run's status names the frame the stopped run had got to, and the keyframe before it its rest scored again
// from only as that, in brackets (app.js, resumedFrom): "resumed from frame 216 (rescored from its keyframe, frame 124)".
function resumedAt(status, frames) {
  const m = /resumed from frame ([\d,]+)(?: \(rescored from its keyframe, frame ([\d,]+)\))?/.exec(status);
  const n = s => Number(s.replace(/,/g, ''));
  return m !== null && n(m[1]) === frames && (m[2] === undefined || n(m[2]) < frames);
}

// A finished run's times real time, the status line's and the shot statistics' Run, the same figure: of the video the
// run read (a resumed run's rest from its keyframe), not the whole video.
async function realTime(page, what, status) {
  const ours = /\(([\d.]+) times real time\)/.exec(status)?.[1];
  const card = /([\d.]+) times real time/.exec(await page.textContent('#stats dl') ?? '')?.[1];
  if (ours === undefined || ours !== card) fail(`${what}: the status says ${ours} times real time, the statistics ${card}`);
  else console.log(`${what}: ${ours} times real time, in the status and the statistics`);
}

// The stopped run shown again (frames read): the sound measured as for a run, its card and waveform there (the demo
// has AAC); the bitrate's cuts on keyframes and the statistics of the part read, the cut-short last shot in no length;
// Forget not styled as a button to press.
async function keptCards(page, what, frames) {
  await page.waitForFunction(() => document.getElementById('sound')?.hidden === false &&
    !document.getElementById('status-text').textContent.includes('measuring the sound'), null, { timeout: 5 * 60 * 1000 })
    .catch(() => {});
  if (await page.locator('#sound').isHidden() || await page.locator('.track.waveform svg').count() !== 1)
    fail(`${what}: no sound card or waveform for the kept run`);
  const bitrate = await page.textContent('#bitrate') ?? '';
  const all = Number(/([\d,]+) keyframes?,?/.exec(bitrate)?.[1].replace(/,/g, ''));
  const elsewhere = Number(/([\d,]+) keyframes? (?:is|are) at no cut/.exec(bitrate)?.[1]);
  if (!bitrate.includes(`In the ${frames.toLocaleString('en')} frames read:`) || !(elsewhere < all - 1))
    fail(`${what}: the bitrate card doesn't count the frames read: ${bitrate}`);
  const stats = await page.textContent('#stats dl') ?? '', shots = Number(/^Shots(\d+)/.exec(stats)?.[1]);
  if (!(shots > 0) || stats.includes(`(shot ${shots})`))
    fail(`${what}: the statistics count the last shot, cut short at the stop, as a length: ${stats}`);
  const style = id => page.$eval(id, b => { const s = getComputedStyle(b); return [s.backgroundColor, s.color, s.borderColor]; });
  const [forget, detect, resume] = [await style('#forget'), await style('#detect'), await style('#resume')];
  if (forget[0] !== 'rgba(0, 0, 0, 0)' || forget[1] === detect[1] || forget[1] === resume[1])
    fail(`${what}: Forget is styled as Detect or Resume: ${forget} against ${detect} and ${resume}`);
  console.log(`${what}: the sound measured, the bitrate and the statistics of the ${frames} frames read, Forget quiet`);
}

// The page's layout once a run is shown (backlog "The page's layout and styling"), at 1280x800 in light: no card in the
// details with more than about 100 px of empty height under it (the other column's longer); every shot's row in view
// of the shots card, no inner scroll, at 15 shots or fewer; no red in a lane (the timeline's tracks: their elements'
// colours, the SVGs' strokes and fills, the canvases' pixels) but the playhead's; the run card's figures in the first
// screen. Then the page as a visitor sees it, at 1280x800 and 390x844, light and dark: <out>/<name>.<width>.<scheme>.png,
// nothing in it wider than the window or than its card.
const VIEWS = [[1280, 800], [390, 844]];
async function looks(page, what, name) {
  const size = page.viewportSize();
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.emulateMedia({ colorScheme: 'light' });
  await page.evaluate(() => window.scrollTo(0, 0));
  // The stills and the lanes' redraws at the new width.
  await page.waitForTimeout(1000);
  const m = await page.evaluate(() => {
    const cards = [...document.querySelectorAll('#details > .card')].filter(c => !c.hidden).map(c => c.getBoundingClientRect());
    let hole = 0;
    for (const c of cards) {
      // The cards beside this one (the other column), and how far the lowest of them goes below it.
      const beside = cards.filter(o => Math.abs(o.left - c.left) > 1 && o.top < c.bottom && o.bottom > c.top);
      const below = cards.some(o => Math.abs(o.left - c.left) <= 1 && o.top >= c.bottom - 1);
      if (!below && beside.length > 0) hole = Math.max(hole, Math.max(...beside.map(o => o.bottom)) - c.bottom);
    }
    const box = document.getElementById('shots-card');
    const rows = document.querySelectorAll('#shots tbody tr').length;
    // A colour's red: a hue within 20 degrees of red, saturated and neither near black nor white, not see-through.
    const red = (r, g, b, a = 1) => {
      const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
      if (a < 0.15 || d < 0.25 || l < 0.15 || l > 0.9) return false;
      const h = max === r ? 60 * (((g - b) / d) % 6) : max === g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
      return (h + 360) % 360 < 20 || (h + 360) % 360 > 340;
    };
    const colours = text => [...text.matchAll(/rgba?\(([^)]*)\)|color\(srgb ([^)]*)\)/g)].map(m => m[1]
      ? m[1].split(/[ ,/]+/).filter(Boolean).map((v, i) => i < 3 ? Number(v) / 255 : Number(v))
      : m[2].split(/[ /]+/).filter(Boolean).map(Number));
    const reds = new Set();
    for (const e of document.querySelectorAll('#timeline .track, #timeline .track *')) {
      const st = getComputedStyle(e);
      for (const prop of ['color', 'backgroundColor', 'backgroundImage', 'borderLeftColor', 'borderTopColor', 'fill', 'stroke']) {
        // A colour that paints nothing: text in an element without text, a border of no width, an SVG paint of none.
        if (prop === 'color' && !e.textContent.trim() || prop.startsWith('border') && parseFloat(st[prop.replace('Color', 'Width')]) === 0 ||
          (prop === 'fill' || prop === 'stroke') && !(e instanceof SVGElement)) continue;
        if (colours(st[prop]).some(c => red(...c))) reds.add(`${e.className.baseVal ?? e.className} ${prop} ${st[prop]}`);
      }
      if (e instanceof HTMLCanvasElement && e.width > 0 && e.height > 0) {
        const px = e.getContext('2d').getImageData(0, 0, e.width, e.height).data;
        for (let i = 0; i < px.length; i += 4) {
          if (red(px[i] / 255, px[i + 1] / 255, px[i + 2] / 255, px[i + 3] / 255)) { reds.add(`canvas ${e.parentElement.className}`); break; }
        }
      }
    }
    const figures = document.querySelector('#run .figures')?.getBoundingClientRect();
    return { hole: Math.round(hole), rows, scroll: box.scrollHeight - box.clientHeight, reds: [...reds],
      figures: figures ? Math.round(figures.bottom) : null };
  });
  if (m.hole > 100) fail(`${what}: a details card has ${m.hole} px of empty height under it`);
  if (m.rows <= 15 && m.scroll > 0) fail(`${what}: the shots card scrolls ${m.scroll} px for ${m.rows} rows`);
  if (m.reds.length > 0) fail(`${what}: red in a lane: ${m.reds.join('; ')}`);
  if (m.figures === null || m.figures > 800) fail(`${what}: the run card's figures end at ${m.figures} px, below the fold`);
  console.log(`${what}: ${m.hole} px at most under a details card, ${m.rows} shot rows unscrolled, no red in a lane, ` +
    `the run's figures down to ${m.figures} px`);
  for (const [width, height] of VIEWS) {
    for (const scheme of ['light', 'dark']) {
      await page.setViewportSize({ width, height });
      await page.emulateMedia({ colorScheme: scheme });
      await page.waitForTimeout(500);
      await page.screenshot({ path: join(outDir, `${name}.${width}.${scheme}.png`), fullPage: true });
      // Nothing wider than the window, nor than its card (the shots table at phone width).
      const wide = await page.evaluate(() => [document.documentElement, ...document.querySelectorAll('.card')]
        .filter(e => e.offsetParent !== null || e === document.documentElement)
        .filter(e => e.scrollWidth > e.clientWidth + 1).map(e => e.id || e.tagName));
      if (scheme === 'light' && wide.length > 0) fail(`${what}: wider than the window or its card at ${width} px: ${wide.join(', ')}`);
    }
  }
  await page.setViewportSize(size ?? { width: 1280, height: 720 });
  await page.emulateMedia({ colorScheme: null });
}

// The page cross-origin isolated: on a first visit, once sw.js has taken over and the page has reloaded (app.js, serve).
async function isolated(page) {
  for (let tries = 0; ; tries++) {
    try {
      await page.waitForFunction(() => globalThis.crossOriginIsolated === true, null, { timeout: 30000 });
      // Isolated from the reloaded document's start, before its modules have run: the page once loaded.
      await page.waitForLoadState('load');
      return;
    } catch (e) {
      // The reload replaced the page's context while it was waited on.
      if (tries > 2 || !/context was destroyed|navigation/i.test(e.message)) throw e;
      await page.waitForLoadState();
    }
  }
}

// A one-worker run's status: on threads unless asked not to (?threads=0), as many as the machine's cores, at most 4.
const THREADED = /on [2-4] threads/;

// Waits for the run to end: done, or any other end state, which fails with what the page says.
async function finished(page, what, expected = 'done') {
  const started = Date.now();
  await page.waitForFunction(() => ['done', 'stopped', 'lost', 'error'].includes(document.body.dataset.state),
    null, { timeout: 15 * 60 * 1000, polling: 250 });
  const state = await page.evaluate(() => document.body.dataset.state);
  const status = await page.textContent('#status-text');
  const seconds = ((Date.now() - started) / 1000).toFixed(1);
  if (state !== expected) {
    fail(`${what}: the page ended ${state}: ${(await page.textContent('#error')) || status}`);
    return null;
  }
  console.log(`${what}: ${state} (${seconds} s): ${status}`);
  return status;
}

// The run's downloads, as a visitor clicks them, against the native check's files of the same run (its CSVs, the
// shots as JSON and HTML and the editors' timeline files); the shots table against the scenes CSV. A run stopped at frame stoppedAt: its stats CSV the
// native run's rows up to that frame, but for the adaptive ratio of the last w frames, None (its window after them
// isn't read), as app-check.mjs's checkStopped; its scenes CSV's last shot ending there, and its JSON and HTML the
// scenes CSV's shots.
async function compare(page, what, name, run, stoppedAt = null) {
  let scenes = null; // the downloaded scenes CSV's shots, [start frame, end frame] each
  // The run's card shows its downloads once it has the shots as JSON and HTML, a call or two after 'done' (on threads,
  // to the threaded runner: a little later than to the worker).
  await page.locator('#scenes-csv').waitFor({ state: 'visible', timeout: 10000 }).catch(() => {});
  for (const [kind, ext, label] of [['scenes', 'csv', 'scenes CSV'], ['stats', 'csv', 'stats CSV'],
    ['shots', 'json', 'shots JSON'], ['shots', 'html', 'shots HTML']]) {
    const link = page.locator(`#${kind}-${ext}`);
    if (!await link.isVisible()) {
      fail(`${what}: no ${label} to download`);
      continue;
    }
    const [download] = await Promise.all([page.waitForEvent('download'), link.click()]);
    const expectedName = `${stem(name)}.${kind}.${ext}`;
    if (download.suggestedFilename() !== expectedName) {
      fail(`${what}: the ${label} downloads as ${download.suggestedFilename()}, not ${expectedName}`);
    }
    const saved = join(outDir, `${what.replace(/\W+/g, '-')}.${kind}.${ext}`);
    await download.saveAs(saved);
    const native = join(nativeDir, `${stem(FILES[name])}.${run}.${kind}.${ext}`);
    if (!existsSync(native)) {
      fail(`${what}: no native ${label} ${native} (tools/check.sh native first)`);
    } else if (stoppedAt !== null && kind === 'shots') {
      // Shots.Json's and Shots.Html's (its last table, a row a shot after the header) shots, the scenes CSV's.
      const text = readFileSync(saved, 'utf8');
      const ours = ext === 'json'
        ? JSON.parse(text).map(s => [s.startFrame, s.endFrame])
        : text.split('<table').at(-1).split('<tr>').slice(2).map(row => [...row.matchAll(/<td>([^<]*)<\/td>/g)])
          .map(cells => [Number(cells[1][1]), Number(cells[4][1])]);
      if (scenes === null || JSON.stringify(ours) !== JSON.stringify(scenes))
        fail(`${what}: the downloaded ${label}'s shots (${saved}) aren't the scenes CSV's`);
      else console.log(`${what}: ${expectedName} has the scenes CSV's ${scenes.length} shots`);
    } else if (stoppedAt !== null) {
      const ours = lines(readFileSync(saved, 'utf8')), theirs = lines(readFileSync(native, 'utf8'));
      if (kind === 'stats') {
        const header = ours[0].split(',');
        const ratios = header.flatMap((h, i) => /^adaptive_ratio.*\(w=\d+\)$/.test(h) ? [i] : []);
        const window = ratios.length > 0 ? Number(/\(w=(\d+)\)$/.exec(header[ratios[0]])[1]) : 0;
        const rows = theirs.filter((line, i) => i === 0 || Number(line.split(',')[0]) <= stoppedAt);
        const same = (a, b) => a === b || Number(a.split(',')[0]) > stoppedAt - window &&
          a.split(',').every((cell, c, all) => all.length === b.split(',').length &&
            (cell === b.split(',')[c] || ratios.includes(c) && cell === 'None'));
        if (ours.length !== rows.length || ours.some((line, i) => !same(line, rows[i]))) {
          fail(`${what}: the downloaded stats CSV (${saved}) isn't ${basename(native)}'s ${rows.length - 1} rows up ` +
            `to frame ${stoppedAt}`);
        } else {
          console.log(`${what}: ${expectedName} is ${basename(native)}'s ${rows.length - 1} rows up to frame ` +
            `${stoppedAt}${window > 0 ? `, the adaptive ratio None in the last ${window}` : ''}`);
        }
      } else if (Number(ours.at(-1).split(',')[4]) !== stoppedAt) {
        fail(`${what}: the scenes CSV's last shot doesn't end at frame ${stoppedAt}: ${ours.at(-1)}`);
      }
    } else if (!readFileSync(saved).equals(readFileSync(native))) {
      fail(`${what}: the downloaded ${label} (${saved}) differs from ${native}`);
    } else {
      console.log(`${what}: ${expectedName} identical to ${basename(native)}`);
    }
    if (kind === 'scenes') {
      // scenedetect's list-scenes CSV: the cuts' timecodes (when there are any) and the header, then a shot a line.
      scenes = lines(readFileSync(saved, 'utf8')).filter(line => /^\d/.test(line)).map(line => line.split(','))
        .map(cells => [Number(cells[1]), Number(cells[4])]);
      const shots = scenes.length;
      const rows = await page.locator('#shots tbody tr').count();
      if (shots < 1 || rows !== shots) fail(`${what}: the shots table has ${rows} rows for the CSV's ${shots} shots`);
    }
  }
  // The editors' timeline files (Api's TimelineFile), each the native run's but for the file's name, which the page
  // gives as the picked file's (the demo's sintel-trailer.mp4, the native check's sample's own). A stopped run's: the
  // EDL a cut event a shot of the scenes CSV, the QP and keyframes files frame 0 and each later shot's start, the shot
  // changes each later shot's start.
  for (const [id, ext, label] of [['timeline-edl', 'edl', 'EDL'], ['timeline-fcpxml', 'fcpxml', 'FCPXML'],
    ['timeline-fcp7', 'fcp7.xml', 'FCP7 XML'], ['timeline-otio', 'otio', 'OTIO'], ['timeline-qp', 'qp', 'QP file'],
    ['timeline-keyframes', 'keyframes.txt', 'keyframes file'], ['timeline-shotchanges', 'shotchanges', 'shot changes']]) {
    const link = page.locator(`#${id}`);
    if (!await link.isVisible()) {
      fail(`${what}: no ${label} to download`);
      continue;
    }
    const download = await clicked(page, link);
    const expectedName = `${stem(name)}.${ext}`;
    if (download.suggestedFilename() !== expectedName) {
      fail(`${what}: the ${label} downloads as ${download.suggestedFilename()}, not ${expectedName}`);
    }
    const saved = join(outDir, `${what.replace(/\W+/g, '-')}.${ext}`);
    await download.saveAs(saved);
    const text = readFileSync(saved, 'utf8');
    const native = join(nativeDir, `${stem(FILES[name])}.${run}.${ext}`);
    if (stoppedAt !== null) {
      if (scenes === null) continue;
      const starts = ext === 'edl'
        ? lines(text).filter(line => /^\d{3}  /.test(line)).length
        : ext === 'qp' ? lines(text).length : ext === 'keyframes.txt' ? lines(text).length - 2
        : ext === 'shotchanges' ? lines(text).length + 1 : null;
      const expected = scenes.length;
      if (starts !== null && starts !== expected) fail(`${what}: the stopped run's ${label} has ${starts} lines for ${expected} shots`);
      else console.log(`${what}: ${expectedName} downloaded${starts !== null ? `, ${starts} lines for the ${expected} shots` : ''}`);
    } else if (!existsSync(native)) {
      fail(`${what}: no native ${label} ${native} (tools/check.sh native first)`);
    } else if (text !== readFileSync(native, 'utf8').replaceAll(stem(FILES[name]), stem(name))) {
      fail(`${what}: the downloaded ${label} (${saved}) differs from ${native}${basename(FILES[name]) === name ? '' : ', its name the page\'s'}`);
    } else {
      console.log(`${what}: ${expectedName} identical to ${basename(native)}${basename(FILES[name]) === name ? '' : ', its name the page\'s'}`);
    }
  }
  if (stoppedAt === null) await compareChapters(page, what, name, run);
}

// A link's download. Chromium drops a click now and then when downloads come one after another within milliseconds
// (seen 2026-10-31 on the seventh timeline file, the shot changes, and on the chapters' link): a second click, as a
// person would make, downloads it.
async function clicked(page, link) {
  const first = await Promise.all([page.waitForEvent('download', { timeout: 5000 }), link.click()]).then(([d]) => d, () => null);
  return first ?? (await Promise.all([page.waitForEvent('download'), link.click()]))[0];
}

// The YouTube chapters (Api's YouTubeChapters, ShotDetector's Timeline's) at the page's default of 30 s and at
// YouTube's 10, typed in the field: the native run's chapters-<minimum>.json, its text downloaded as
// <file>.chapters.txt or, when there is none, its reason shown in its place; then, at 10 s, a title typed for the second
// chapter, which the download then carries in that line (the rest Timeline's). The field is left at 30 again.
async function compareChapters(page, what, name, run) {
  const field = page.locator('#chapters-min');
  if (!await field.isVisible()) {
    fail(`${what}: no YouTube chapters' minimum`);
    return;
  }
  if (await field.inputValue() !== '30') fail(`${what}: the chapters' minimum reads ${await field.inputValue()}, not 30`);
  for (const minimum of [30, 10]) {
    const native = join(nativeDir, `${stem(FILES[name])}.${run}.chapters-${minimum}.json`);
    if (!existsSync(native)) {
      fail(`${what}: no native chapters ${native} (tools/check.sh native first)`);
      continue;
    }
    const expected = JSON.parse(readFileSync(native, 'utf8'));
    if (minimum !== 30) await field.fill(String(minimum));
    // Shown once the row says this minimum's: its reason, or the link's title its count.
    const shown = await page.waitForFunction(({ text, notMade, count, minimum }) => {
      const link = document.querySelector('#chapters-txt'), none = document.querySelector('#chapters-none');
      return text === null
        ? link.hidden && none.textContent === notMade
        : !link.hidden && !!link.href && link.title.includes(`${count} chapters of at least ${minimum} s`);
    }, { text: expected.text, notMade: expected.notMade, count: expected.starts.length, minimum }, { timeout: 10000 })
      .then(() => true, () => false);
    if (!shown) {
      fail(`${what}: the chapters at ${minimum} s aren't shown as ${basename(native)} has them: ` +
        `${await page.textContent('#run .chapters')}`);
    } else if (expected.text === null) {
      console.log(`${what}: no chapters at ${minimum} s, as ${basename(native)}: ${expected.notMade}`);
    } else {
      // A click within milliseconds of the link's new blob URL downloaded nothing in Chromium, a second click did
      // (seen 2026-10-31, after the run's other downloads); a person doesn't click that fast.
      await page.waitForTimeout(1000);
      const [download] = await Promise.all([page.waitForEvent('download'), page.locator('#chapters-txt').click()]);
      const expectedName = `${stem(name)}.chapters.txt`;
      if (download.suggestedFilename() !== expectedName)
        fail(`${what}: the chapters download as ${download.suggestedFilename()}, not ${expectedName}`);
      const saved = join(outDir, `${what.replace(/\W+/g, '-')}.chapters-${minimum}.txt`);
      await download.saveAs(saved);
      if (readFileSync(saved, 'utf8') !== expected.text) fail(`${what}: the chapters at ${minimum} s (${saved}) aren't ${basename(native)}'s`);
      else console.log(`${what}: ${expectedName} at ${minimum} s, ${expected.starts.length} chapters, ${basename(native)}'s`);
      if (minimum === 10) await checkTitles(page, what, expected, saved);
    }
  }
  await field.fill('30');
}

// A title typed for the second chapter (under Titles): the download's second line is its start and that title, the
// others Timeline's. The title is cleared again.
async function checkTitles(page, what, expected, saved) {
  const title = 'Le dragon  arrive';
  await page.locator('#run .chapter-titles summary').click();
  const input = page.locator('#run .chapter-titles li').nth(1).locator('input');
  if (await input.getAttribute('placeholder') !== 'Chapter 2') fail(`${what}: the second chapter's title field reads ${await input.getAttribute('placeholder')}`);
  await input.fill(title);
  await page.waitForFunction(() => document.querySelector('#chapters-txt').title.includes('1 titled on the page'), null, { timeout: 10000 })
    .catch(() => fail(`${what}: the chapters' link doesn't say a title was typed`));
  await page.waitForTimeout(1000);
  const [download] = await Promise.all([page.waitForEvent('download'), page.locator('#chapters-txt').click()]);
  const titledFile = saved.replace(/\.txt$/, '.titled.txt');
  await download.saveAs(titledFile);
  const want = expected.text.split('\n').map((line, i) => i === 1 ? `${line.slice(0, line.indexOf(' '))} Le dragon arrive` : line).join('\n');
  if (readFileSync(titledFile, 'utf8') !== want) fail(`${what}: the chapters with a title typed (${titledFile}) aren't ${JSON.stringify(want)}`);
  else console.log(`${what}: the chapters' download carries the title typed: ${want.split('\n')[1]}`);
  await input.fill('');
  await page.locator('#run .chapter-titles summary').click();
}

// Pauses the run once its bar reaches from (a fraction): the button reads Resume, and once the run has reached the
// pause it waits at (up to 8 frames on, Api's PauseEvery), the bar and the status hold still. The frame it holds at,
// or null.
async function paused(page, what, from) {
  await page.waitForFunction(f => document.body.dataset.state === 'detecting' &&
    document.getElementById('progress').value >= f, from, { timeout: 15 * 60 * 1000, polling: 50 });
  await page.click('#pause');
  const held = () => page.evaluate(() => [document.body.dataset.state, document.getElementById('progress').value,
    document.getElementById('status-text').textContent, document.getElementById('pause').textContent]);
  await page.waitForTimeout(1000);
  const before = await held();
  await page.waitForTimeout(2000);
  const after = await held();
  const [state, , status, button] = after;
  if (state !== 'paused' || button !== 'Resume') {
    fail(`${what}: not paused (state ${state}, the button reading ${button})`);
    return null;
  }
  if (JSON.stringify(before) !== JSON.stringify(after)) {
    fail(`${what}: the run went on while paused: ${JSON.stringify(before)} then ${JSON.stringify(after)}`);
    return null;
  }
  const frame = Number(/Paused at frame ([\d,]+)/.exec(status)?.[1].replace(/,/g, ''));
  console.log(`${what}: held at frame ${frame} for 2 s: ${status}`);
  return frame;
}

// The picture's card after a whole run: its cropdetect line and its colour bars the native check's
// (<sample>.picture.json, checked there against FFmpeg's filters), and the lane shown.
async function picture(page, what, file) {
  const native = join(nativeDir, `${stem(file)}.picture.json`);
  if (!existsSync(native)) {
    fail(`${what}: no native ${native} (tools/check.sh native first)`);
    return;
  }
  const p = JSON.parse(readFileSync(native, 'utf8'));
  await page.waitForFunction(() => document.getElementById('picture')?.hidden === false &&
    document.querySelector('#picture h3') !== null, null, { timeout: 60 * 1000 }).catch(() => {});
  const text = await page.textContent('#picture') ?? '';
  const crop = p.crop ? `crop=${p.crop.w}:${p.crop.h}:${p.crop.x}:${p.crop.y}` : 'Too few frames for cropdetect.';
  const bars = (text.match(/Colour bars: \d/g) ?? []).length;
  const lane = await page.locator('.track.picture svg').count();
  // Each line a sentence of its own: none goes on from the one before (as "and 3 shorter" after "none").
  const dangling = await page.$$eval('#picture p', ps => ps.map(p => p.textContent).filter(t => /^(and|or|but) /.test(t)));
  if (dangling.length > 0) fail(`${what}: the picture's card has lines that aren't sentences: ${dangling.join(' | ')}`);
  if (!text.includes(crop) || bars !== p.bars.length || lane !== 1)
    fail(`${what}: the picture's card says "${text.slice(0, 300)}" (${bars} colour bars, ${lane} lane), not ${crop} and ${p.bars.length} colour bars`);
  else
    console.log(`${what}: the picture's card: ${crop}, ${bars} colour bars, as the native check's`);
}

// The frame types' card after a whole run: the counts of ffprobe's types (<sample>.frametypes.txt, which the native
// check compared the core's with), and the lane shown.
async function frameTypes(page, what, file) {
  const native = join(nativeDir, `${stem(file)}.frametypes.txt`);
  if (!existsSync(native)) {
    fail(`${what}: no native ${native} (tools/check.sh native first)`);
    return;
  }
  const types = readFileSync(native, 'utf8'), n = t => (types.split(t).length - 1).toLocaleString('en');
  const expected = `${types.length.toLocaleString('en')} frames: ${n('I')} I, ${n('P')} P, ${n('B')} B`;
  await page.waitForFunction(() => document.getElementById('frametypes')?.hidden === false &&
    document.querySelector('#frametypes p') !== null, null, { timeout: 60 * 1000 }).catch(() => {});
  const text = await page.textContent('#frametypes') ?? '';
  const lane = await page.locator('.track.frametypes canvas').count();
  if (!text.includes(expected) || lane !== 1)
    fail(`${what}: the frame types' card says "${text.slice(0, 200)}" (${lane} lane), not "${expected}"`);
  else
    console.log(`${what}: the frame types' card: ${expected}, ffprobe's`);
}

// A variable-rate video's ranges lane (Open's frameTimestamps): the second band starts where its first frame's own pts
// puts it on the axis, from ffprobe's packets as the native check wrote them (the shown ones, sorted), not where its
// number over the average rate would.
async function rangesAtTheirTimes(page, what, file) {
  const video = JSON.parse(await page.textContent('#about pre'));
  if (!video.frameTimestamps) return;
  const native = join(nativeDir, `${stem(file)}.packets.txt`);
  if (!existsSync(native)) return fail(`${what}: no native ${native} (tools/check.sh native first)`);
  const times = lines(readFileSync(native, 'utf8')).filter(l => !/flags=[^|]*D/.test(l))
    .map(l => Number(/pts_time=([^|]+)/.exec(l)[1])).sort((a, b) => a - b);
  const band = page.locator('.range').nth(1);
  const [left, title] = [await band.evaluate(b => parseFloat(b.style.left)), await band.getAttribute('title')];
  const first = Number(/frames (\d+) to/.exec(title)[1]);
  const at = (times[first] - times[0]) / video.duration * 100;
  const byRate = first * video.frameRate.den / video.frameRate.num / video.duration * 100;
  if (!(Math.abs(left - at) < 0.01) || Math.abs(byRate - at) < 0.5)
    fail(`${what}: the second range's band starts at ${left}%, not ${at.toFixed(3)}% (frame ${first}'s pts; ${byRate.toFixed(3)}% by the average rate)`);
  else console.log(`${what}: the second range (from frame ${first}) at ${left.toFixed(3)}%, its frame's own time`);
}

function lines(csv) {
  return csv.split(/\r?\n/).filter(line => line !== '');
}

function report(what, errors) {
  if (errors.length > 0) fail(`${what}: errors on the page:\n  ${errors.join('\n  ')}`);
}

// deinterlace: the Deinterlace option picked ('auto', 'on'), compared with the native check's deinterlace-<it> run; a
// deinterlaced run asked to split goes on one worker, the ranges lane saying why.
async function picked(query, what, file = sample, deinterlace = null) {
  const { context, page, errors } = await open(query);
  await page.setInputFiles('#file', file);
  if (deinterlace) await page.selectOption('#deinterlace', deinterlace);
  await page.click('#detect');
  const run = deinterlace ? `deinterlace-${deinterlace}` : await page.inputValue('#detector');
  const status = await finished(page, what);
  if (status !== null) {
    const split = query.includes('workers=');
    if (split && deinterlace) {
      const line = await page.locator('.track.ranges').textContent() ?? '';
      if (/with 2 workers/.test(status) || await page.locator('.range').count() !== 0 || !/Deinterlaced, so not split/.test(line))
        fail(`${what}: deinterlaced, not on one worker saying why: "${status}", the ranges lane "${line}"`);
      else console.log(`${what}: one worker, the ranges lane saying "${line}"`);
    } else if (split && !/with 2 workers/.test(status)) fail(`${what}: not split across 2 workers: ${status}`);
    if (!split && THREADED.test(status) !== (query === '')) fail(`${what}: ${query ? 'on' : 'not on'} threads: ${status}`);
    if (split && !deinterlace) {
      const bands = await page.locator('.range').count();
      if (bands !== 2) fail(`${what}: the ranges lane has ${bands} bands, not 2`);
      else await rangesAtTheirTimes(page, what, file);
    }
    await compare(page, what, basename(file), run);
    await picture(page, what, file);
    await frameTypes(page, what, file);
  }
  report(what, errors);
  await context.close();
}

// An AVI picked (MJPEG, which no browser plays): the player the file's own JPEG at the playhead, in the <video>'s place,
// following the timeline and the shots' rows; a still per shot from the same JPEGs; the downloads the native run's.
async function pickedAvi() {
  const what = `${basename(avi)}, picked`;
  const { context, page, errors } = await open();
  await page.setInputFiles('#file', avi);
  await page.click('#detect');
  const run = await page.inputValue('#detector');
  const status = await finished(page, what);
  if (status !== null) {
    await compare(page, what, basename(avi), run);
    await picture(page, what, avi);
    await frameTypes(page, what, avi);
    // The rate as Open describes the file (the about card's JSON).
    const { frameRate: { num, den } } = JSON.parse(await page.textContent('#about pre'));
    // The frame the page shows: the <img>'s, once decoded, and where the playhead is.
    const shown = () => page.waitForFunction(() => {
      const img = document.getElementById('frame');
      return !img.hidden && img.complete && img.naturalWidth > 0 && img.dataset.frame !== undefined;
    }, null, { timeout: 10000 }).then(() => page.evaluate(() => {
      const img = document.getElementById('frame');
      return { frame: Number(img.dataset.frame), width: img.naturalWidth, height: img.naturalHeight,
        video: !document.getElementById('video').hidden,
        at: Number(document.querySelector('#timeline .tracks').getAttribute('aria-valuenow')) };
    })).catch(() => null);
    const expectFrame = async (where, frame) => {
      // The <img> follows a new frame as soon as its JPEG is cut and decoded.
      await page.waitForFunction(f => document.getElementById('frame').dataset.frame === String(f), frame,
        { timeout: 10000 }).catch(() => {});
      const s = await shown();
      if (s === null || s.video || s.frame !== frame || s.width !== 320 || s.height !== 240)
        fail(`${what}: ${where}, the picture isn't frame ${frame}'s 320x240 JPEG in the video's place: ${JSON.stringify(s)}`);
      else console.log(`${what}: ${where}, the picture is frame ${frame}, ${s.width}x${s.height}, the playhead at ${s.at} s`);
    };
    await expectFrame('at the start', 0);
    // A click on the timeline at 60% of its width: the frame the playhead is in (the page's own times, frames over the
    // rate as the axis has them).
    const tracks = page.locator('#timeline .tracks');
    await tracks.scrollIntoViewIfNeeded();
    const box = await tracks.boundingBox();
    await tracks.click({ position: { x: box.width * 0.6, y: box.height / 2 } });
    const at = Number(await tracks.getAttribute('aria-valuenow'));
    if (!(at > 0)) fail(`${what}: a click on the timeline left the playhead at ${at} s`);
    await expectFrame(`clicked at ${at} s`, Math.floor(at * num / den + 1e-6));
    // A shot's row (the third, else the last): its first frame, the scenes CSV's start frame (counted from 1).
    const rows = await page.locator('#shots tbody tr').count();
    const row = page.locator('#shots tbody tr').nth(Math.min(2, rows - 1));
    const start = await row.locator('td').nth(2).textContent();
    const scenes = lines(readFileSync(join(nativeDir, `${stem(avi)}.${run}.scenes.csv`), 'utf8'))
      .filter(line => /^\d/.test(line)).map(line => line.split(','));
    const shot = scenes.find(cells => cells[2] === start);
    await row.click();
    if (!shot) fail(`${what}: no shot in the scenes CSV starts at ${start}`);
    else await expectFrame(`the shot from ${start}`, Number(shot[1]) - 1);
    // A still per shot, every one drawn from the file's JPEGs (none "didn't show").
    await page.waitForFunction(n => document.querySelectorAll('#shots td.still canvas').length >= n, rows,
      { timeout: 30000 }).catch(() => {});
    const stills = await page.$$eval('#shots td.still', cells => cells.map(c => {
      const canvas = c.querySelector('canvas');
      if (!canvas) return c.title || 'none';
      const px = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
      let lit = 0;
      for (let i = 0; i < px.length; i += 4) lit += px[i + 3] > 0 && px[i] + px[i + 1] + px[i + 2] > 0 ? 1 : 0;
      return lit > 0 ? 'drawn' : 'blank';
    }));
    const missing = stills.flatMap((s, i) => s === 'drawn' ? [] : [`shot ${i + 1}: ${s}`]);
    if (stills.length !== rows || missing.length > 0) fail(`${what}: ${rows} shots, stills: ${missing.join('; ')}`);
    else console.log(`${what}: a still for each of the ${rows} shots, from the file's JPEGs`);
    await looks(page, what, 'the-avi.done');
  }
  report(what, errors);
  await context.close();
}

try {
  await pickedAvi();
  await picked('', `${basename(sample)}, picked`);
  await picked('?threads=0', `${basename(sample)}, the page's worker`);
  await picked('?workers=2', `${basename(sample)}, two workers`);
  for (const m of more) {
    await picked('', `${basename(m)}, picked`, m);
    await picked('?workers=2', `${basename(m)}, two workers`, m);
  }
  if (interlaced) {
    await picked('', `${basename(interlaced)}, deinterlaced, picked`, interlaced, 'auto');
    await picked('?workers=2', `${basename(interlaced)}, deinterlaced, two workers asked`, interlaced, 'auto');
  }

  const what = 'the demo';
  const { context, page, errors } = await open();
  await page.click('#demo');
  const run = await page.inputValue('#detector');
  if (await paused(page, `${what} paused`, 0.25) !== null) {
    await page.click('#pause');
    if (await page.evaluate(() => document.body.dataset.state) !== 'detecting') fail(`${what}: Resume didn't resume`);
  }
  const status = await finished(page, what);
  if (status !== null) {
    if (!THREADED.test(status)) fail(`${what}: not on threads: ${status}`);
    await compare(page, what, 'sintel-trailer.mp4', run);
    await picture(page, what, demo);
    await frameTypes(page, what, demo);
    // The sound's worker, alongside the run: done when the status no longer says it is measuring.
    await page.waitForFunction(() => !document.getElementById('status-text').textContent.includes('measuring the sound'),
      null, { timeout: 5 * 60 * 1000 });
    console.log(`${what}: the sound measured`);
    await looks(page, what, 'the-demo.done');

    // Detected again, paused and stopped while paused: the result for the frames read up to the pause.
    const again = 'the demo stopped while paused';
    await page.click('#detect');
    const at = await paused(page, again, 0.15);
    if (at !== null) {
      await page.click('#stop');
      const status = await finished(page, again, 'stopped');
      if (status !== null) {
        const frames = Number(/frame ([\d,]+) of/.exec(status)?.[1].replace(/,/g, ''));
        // The paused status names the frame the run is held at (the worker's 'held': the frames read at that
        // pause), which is where a stop ends it.
        if (frames !== at)
          fail(`${again}: stopped at frame ${frames}, not at ${at}, where it said it was held: ${status}`);
        await compare(page, again, 'sintel-trailer.mp4', run, frames);
        if (!await page.isVisible('#resume')) fail(`${again}: no Resume offered`);
        await resumedAfterReload(page, 'the demo across a reload', frames, run, isolated, true);
      }
    }

    // Offline: the page and both bundles from the service worker's cache, the demo too (fetched above).
    const offline = 'the demo offline';
    await context.setOffline(true);
    await page.reload();
    await isolated(page);
    await page.click('#demo');
    const offlineStatus = await finished(page, offline);
    if (offlineStatus !== null) {
      if (!THREADED.test(offlineStatus)) fail(`${offline}: not on threads: ${offlineStatus}`);
      await compare(page, offline, 'sintel-trailer.mp4', run);
    }
  }
  report(what, errors);
  await context.close();

  // Split across two workers, stopped and resumed in the same visit: the rest split again.
  {
    const what = 'the demo on two workers, resumed';
    const { context, page, errors } = await open('?workers=2');
    await page.click('#demo');
    const frames = await stopped(page, `${what}, stopped`, 0.4);
    if (frames !== null) {
      await page.click('#resume');
      const status = await finished(page, what);
      if (status !== null) {
        if (!/with 2 workers/.test(status) || !resumedAt(status, frames))
          fail(`${what}: not resumed on 2 workers from frame ${frames}: ${status}`);
        await realTime(page, what, status);
        await compare(page, what, 'sintel-trailer.mp4', await page.inputValue('#detector'));
      }
    }
    report(what, errors);
    await context.close();
  }

  if (process.env.PAGE_CHECK_FIREFOX === 'skip') {
    console.log('Firefox: left out (PAGE_CHECK_FIREFOX=skip)');
  } else {
    const fox = await firefox.launch();
    try {
      const what = 'Firefox';
      const { context, page, errors } = await open('?threads=0', fox);
      await page.click('#demo');
      const run = await page.inputValue('#detector');
      const frames = await stopped(page, `${what}, the demo stopped`, 0.3);
      if (frames !== null) await resumedAfterReload(page, `${what}, the demo across a reload`, frames, run, served, false);
      report(what, errors);
      await context.close();
    } finally {
      await fox.close();
    }
  }
} finally {
  await browser.close();
  server.close();
}

if (failures.length > 0) {
  console.log(`The page check failed: ${failures.length} ${failures.length === 1 ? 'failure' : 'failures'}.`);
  process.exit(1);
}
console.log('The page check passed: isolated, picked on threads, on the page\'s worker and split across two workers, ' +
  'the demo on threads paused and resumed, and offline, the CSVs, JSON and HTML identical to the native run\'s; the ' +
  'demo stopped while paused, the result for the frames read, and resumed across a reload' +
  `${process.env.PAGE_CHECK_FIREFOX === 'skip' ? '' : ' (in Firefox too)'} and split.`);
