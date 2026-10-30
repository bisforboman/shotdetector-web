// The page's smoke test (README "Checks", page; decision 2026-10-24-the-page.md, "Testing the page"): the published
// wwwroot served as Pages serves it, with the demo clip in demo/ (pages.yml's "Make the site"), and the page itself in
// Chromium through Playwright, its own JavaScript (app.js, worker.js, the panels) as a visitor drives it:
//   1. a sample set on the file input, detected with the options as the page prefills them, its scenes and stats CSVs
//      downloaded from the run's card and compared with the native check's run of that detector byte for byte, the
//      shots table one row per shot of the CSV;
//   2. the same sample split across two workers (?workers=2): the ranges lane, and the same CSVs;
//   3. "Try the demo": the clip fetched and run as picked, paused a quarter of the way in (the bar and the status held
//      still, the button reading Resume), resumed, the same comparison, the sound measured;
//   4. the demo detected again, paused and stopped while paused: it ends stopped at the pause it was held at, its
//      stats CSV the native run's first rows up to there, the shots table a row per shot of its scenes CSV.
// After each whole run (1 to 3), the picture's card (panels/picture.js) shows the native check's cropdetect line and
// colour bars, and its lane is there.
// Any error on the page, in its workers or a failed request fails the check.
//
// Usage: node tools/page-check/page-check.mjs <wwwroot> <out> <sample.mp4> <demo.mp4> --native <out/native>
//   Playwright from tools/page-check/node_modules (npm ci there; its Chromium: npx playwright install chromium), or
//   else wherever require finds it (a cloud session's pre-installed one). <out> gets the downloaded CSVs.
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, extname, join, normalize, resolve } from 'node:path';

const args = process.argv.slice(2);
const at = args.indexOf('--native');
if (at < 0 || args.length !== 6) {
  console.error('Usage: node page-check.mjs <wwwroot> <out> <sample.mp4> <demo.mp4> --native <out/native>');
  process.exit(2);
}
const nativeDir = resolve(args[at + 1]);
const [root, outDir, sample, demo] = args.filter((_, i) => i !== at && i !== at + 1).map(a => resolve(a));
mkdirSync(outDir, { recursive: true });

let playwright;
try {
  playwright = await import('playwright');
} catch {
  playwright = createRequire(import.meta.url)('playwright');
}
const { chromium } = playwright;

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

// A fresh page each time, as a visitor's first visit (no rate kept from an earlier run), its errors collected:
// exceptions, console errors (the workers' too) and failed or missing requests.
async function open(query = '') {
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  const errors = [];
  const watch = (where, target) => {
    target.on('console', m => { if (m.type() === 'error') errors.push(`${where} console: ${m.text()}`); });
    target.on('pageerror', e => errors.push(`${where}: ${e.message}`));
  };
  watch('page', page);
  page.on('worker', w => w.on('console', m => { if (m.type() === 'error') errors.push(`worker console: ${m.text()}`); }));
  // The <video> aborts its own range requests of the file's blob URL as it seeks and buffers: not an error.
  page.on('requestfailed', r => {
    const why = r.failure()?.errorText;
    if (!(r.resourceType() === 'media' && why === 'net::ERR_ABORTED')) errors.push(`request failed: ${r.url()} (${why})`);
  });
  page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()}: ${r.url()}`); });
  await page.goto(base + query);
  return { context, page, errors };
}

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

// The run's downloads, as a visitor clicks them, against the native check's CSVs of the same run; the shots table
// against the scenes CSV. A run stopped at frame stoppedAt: its stats CSV the native run's rows up to that frame, but
// for the adaptive ratio of the last w frames, None (its window after them isn't read), as app-check.mjs's checkStopped;
// its scenes CSV's last shot ending there.
async function compare(page, what, name, run, stoppedAt = null) {
  for (const kind of ['scenes', 'stats']) {
    const link = page.locator(`#${kind}-csv`);
    if (!await link.isVisible()) {
      fail(`${what}: no ${kind} CSV to download`);
      continue;
    }
    const [download] = await Promise.all([page.waitForEvent('download'), link.click()]);
    const expectedName = `${basename(name, '.mp4')}.${kind}.csv`;
    if (download.suggestedFilename() !== expectedName) {
      fail(`${what}: the ${kind} CSV downloads as ${download.suggestedFilename()}, not ${expectedName}`);
    }
    const saved = join(outDir, `${what.replace(/\W+/g, '-')}.${kind}.csv`);
    await download.saveAs(saved);
    const native = join(nativeDir, `${basename(name === 'sintel-trailer.mp4' ? demo : sample, '.mp4')}.${run}.${kind}.csv`);
    if (!existsSync(native)) {
      fail(`${what}: no native CSV ${native} (tools/check.sh native first)`);
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
      fail(`${what}: the downloaded ${kind} CSV (${saved}) differs from ${native}`);
    } else {
      console.log(`${what}: ${expectedName} identical to ${basename(native)}`);
    }
    if (kind === 'scenes') {
      // scenedetect's list-scenes CSV: the cuts' timecodes (when there are any) and the header, then a shot a line.
      const shots = lines(readFileSync(saved, 'utf8')).filter(line => /^\d/.test(line)).length;
      const rows = await page.locator('#shots tbody tr').count();
      if (shots < 1 || rows !== shots) fail(`${what}: the shots table has ${rows} rows for the CSV's ${shots} shots`);
    }
  }
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
  const native = join(nativeDir, `${basename(file, '.mp4')}.picture.json`);
  if (!existsSync(native)) {
    fail(`${what}: no native ${native} (tools/check.sh native first)`);
    return;
  }
  const p = JSON.parse(readFileSync(native, 'utf8'));
  await page.waitForFunction(() => document.getElementById('picture')?.hidden === false &&
    document.querySelector('#picture .note') !== null, null, { timeout: 60 * 1000 }).catch(() => {});
  const text = await page.textContent('#picture') ?? '';
  const crop = p.crop ? `crop=${p.crop.w}:${p.crop.h}:${p.crop.x}:${p.crop.y}` : 'Too few frames for cropdetect.';
  const bars = (text.match(/Colour bars: \d/g) ?? []).length;
  const lane = await page.locator('.track.picture svg').count();
  if (!text.includes(crop) || bars !== p.bars.length || lane !== 1)
    fail(`${what}: the picture's card says "${text.slice(0, 300)}" (${bars} colour bars, ${lane} lane), not ${crop} and ${p.bars.length} colour bars`);
  else
    console.log(`${what}: the picture's card: ${crop}, ${bars} colour bars, as the native check's`);
}

function lines(csv) {
  return csv.split(/\r?\n/).filter(line => line !== '');
}

function report(what, errors) {
  if (errors.length > 0) fail(`${what}: errors on the page:\n  ${errors.join('\n  ')}`);
}

async function picked(query, what) {
  const { context, page, errors } = await open(query);
  await page.setInputFiles('#file', sample);
  await page.click('#detect');
  const run = await page.inputValue('#detector');
  const status = await finished(page, what);
  if (status !== null) {
    if (query && !/with 2 workers/.test(status)) fail(`${what}: not split across 2 workers: ${status}`);
    if (query) {
      const bands = await page.locator('.range').count();
      if (bands !== 2) fail(`${what}: the ranges lane has ${bands} bands, not 2`);
    }
    await compare(page, what, basename(sample), run);
    await picture(page, what, sample);
  }
  report(what, errors);
  await context.close();
}

try {
  await picked('', `${basename(sample)}, picked`);
  await picked('?workers=2', `${basename(sample)}, two workers`);

  const what = 'the demo';
  const { context, page, errors } = await open();
  await page.click('#demo');
  const run = await page.inputValue('#detector');
  if (await paused(page, `${what} paused`, 0.25) !== null) {
    await page.click('#pause');
    if (await page.evaluate(() => document.body.dataset.state) !== 'detecting') fail(`${what}: Resume didn't resume`);
  }
  if (await finished(page, what) !== null) {
    await compare(page, what, 'sintel-trailer.mp4', run);
    await picture(page, what, demo);
    // The sound's worker, alongside the run: done when the status no longer says it is measuring.
    await page.waitForFunction(() => !document.getElementById('status-text').textContent.includes('measuring the sound'),
      null, { timeout: 5 * 60 * 1000 });
    console.log(`${what}: the sound measured`);

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
      }
    }
  }
  report(what, errors);
  await context.close();
} finally {
  await browser.close();
  server.close();
}

if (failures.length > 0) {
  console.log(`The page check failed: ${failures.length} ${failures.length === 1 ? 'failure' : 'failures'}.`);
  process.exit(1);
}
console.log('The page check passed: picked, split across two workers and the demo paused and resumed, the CSVs ' +
  'identical to the native run\'s; the demo stopped while paused, the result for the frames read.');
