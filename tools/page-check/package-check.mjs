// The npm package's check (tools/check.sh package; decision docs/decisions/2026-11-02-npm-package.md): the packed
// tarball (npm pack of tools/package.sh's folder), unpacked into node_modules/shotdetector-web/ of a test site, and a
// page of that site that imports it as a site would, in Chromium through Playwright:
//   1. detectShots on each sample with each bundle (aot, interpreted): its shots, scenes CSV, stats CSV, shots JSON and
//      HTML the native check's run byte for byte (the sample's run named after it), a still per shot (a JPEG);
//   2. threads: on a cross-origin isolated origin (COOP/COEP) the run on two threads (result.threads 2), the same files;
//      on one that isn't, asked for all the same, the worker's (result.threads 1);
//   3. a ShotDetector stopped half-way (stopped, its stats CSV the native run's rows up to there), one paused (no progress
//      while paused) and resumed (the native run's files), one cancelled through an AbortSignal (an AbortError) and then
//      used again;
//   4. nothing left in the host page's document, no error on the page or in its workers.
// With --replaced: the tarball was packed with a FastYuv built to throw MARK (tools/check.sh replace), and every bundle
// must fail with it: the package's replaceability (LGPL-2.1).
//
// Usage: node tools/page-check/package-check.mjs <shotdetector-web-x.y.z.tgz> --native <out/native> [--replaced]
//          <sample>:<run>...
import { createServer } from 'node:http';
import { execFileSync } from 'node:child_process';
import { createReadStream, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { createRequire } from 'node:module';
import { basename, extname, join, normalize, resolve } from 'node:path';

const MARK = 'This is the replaced ShotDetector.FastYuv.';
const args = process.argv.slice(2);
const at = args.indexOf('--native');
const replaced = args.includes('--replaced');
if (at < 0 || args.length < 4) {
  console.error('Usage: node package-check.mjs <package.tgz> --native <out/native> [--replaced] <sample>:<run>...');
  process.exit(2);
}
const nativeDir = resolve(args[at + 1]);
const [tarball, ...runs] = args.filter((a, i) => i !== at && i !== at + 1 && a !== '--replaced');
const samples = runs.map(r => {
  const i = r.lastIndexOf(':');
  return { file: resolve(r.slice(0, i)), run: r.slice(i + 1) };
});
const RUNS = {
  adaptive: {},
  all: { detectors: ['adaptive', 'content', 'threshold', 'histogram', 'hash'].map(kind => ({ kind })) },
};

let playwright;
try {
  playwright = await import('playwright');
} catch {
  playwright = createRequire(import.meta.url)('playwright');
}

// The test site: the tarball unpacked as npm installs it, the samples, and a page that imports the package.
const site = mkdtempSync(join(tmpdir(), 'package-check-'));
const installed = join(site, 'node_modules', 'shotdetector-web');
mkdirSync(installed, { recursive: true });
execFileSync('tar', ['-xzf', resolve(tarball), '-C', installed, '--strip-components=1']);
writeFileSync(join(site, 'index.html'), `<!doctype html><meta charset="utf-8"><title>package check</title>
<script type="module">
import * as sd from '/node_modules/shotdetector-web/index.js';
window.sd = sd;
window.ready = true;
</script>`);
const sampleDir = join(site, 'samples');
mkdirSync(sampleDir);
for (const { file } of samples) writeFileSync(join(sampleDir, basename(file)), readFileSync(file));

const TYPES = {
  '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
  '.mp4': 'video/mp4', '.avi': 'video/x-msvideo', '.dat': 'application/octet-stream',
};
// Two origins: one cross-origin isolated (COOP/COEP, as a site would set for threads), one not.
async function serve(isolated) {
  const server = createServer((req, res) => {
    const path = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    const file = join(site, normalize(path === '/' ? '/index.html' : path));
    if (!file.startsWith(site) || !existsSync(file) || !statSync(file).isFile()) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, {
      'Content-Type': TYPES[extname(file)] ?? 'application/octet-stream',
      'Content-Length': statSync(file).size,
      ...(isolated ? { 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' } : {}),
    });
    createReadStream(file).pipe(res);
  });
  await new Promise(ok => server.listen(0, '127.0.0.1', ok));
  return { server, url: `http://127.0.0.1:${server.address().port}/` };
}
const plain = await serve(false), isolated = await serve(true);

const browser = await playwright.chromium.launch();
const failures = [];
const fail = message => { failures.push(message); console.log(`FAIL ${message}`); };
const pass = message => console.log(`ok   ${message}`);

async function open(origin) {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(`page: ${e.message}`));
  page.on('console', m => { if (m.type() === 'error') errors.push(`page console: ${m.text()}`); });
  page.on('worker', w => w.on('console', m => { if (m.type() === 'error') errors.push(`worker console: ${m.text()}`); }));
  page.on('requestfailed', r => errors.push(`request failed: ${r.url()} ${r.failure()?.errorText}`));
  page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()}: ${r.url()}`); });
  await page.goto(origin);
  await page.waitForFunction(() => window.ready === true);
  return { page, errors };
}

const native = (file, run, kind) => readFileSync(join(nativeDir, `${basename(file, extname(file))}.${run}.${kind}`), 'utf8');

function same(what, got, want) {
  if (got === want) return true;
  const g = got.split('\n'), w = want.split('\n');
  const i = g.findIndex((line, n) => line !== w[n]);
  fail(`${what}: differs at line ${i + 1}: got ${JSON.stringify(g[i])}, want ${JSON.stringify(w[i])}`);
  return false;
}

// detectShots in the page: the result's JSON-able parts, the files as text, the stills' types and sizes.
async function detect(page, file, run, settings) {
  return page.evaluate(async ({ name, options }) => {
    const blob = await (await fetch(`/samples/${name}`)).blob();
    const file = new File([blob], name);
    const before = document.body.childElementCount + document.head.childElementCount;
    try {
      const r = await window.sd.detectShots(file, { ...options, outputs: ['scenesCsv', 'statsCsv', 'shotsJson', 'shotsHtml'] });
      return {
        shots: r.shots, threads: r.threads, stopped: r.stopped, frames: r.frames,
        scenes: r.files.scenesCsv, stats: await r.files.statsCsv.text(), json: r.files.shotsJson, html: r.files.shotsHtml,
        stills: r.stills.map(s => s && { type: s.type, size: s.size }),
        left: document.body.childElementCount + document.head.childElementCount - before,
      };
    } catch (e) {
      return { error: `${e.name}: ${e.message}` };
    }
  }, { name: basename(file), options: { ...RUNS[run], ...settings } });
}

function compare(label, r, file, run) {
  if (r.error) { fail(`${label}: ${r.error}`); return; }
  let ok = same(`${label} scenes CSV`, r.scenes, native(file, run, 'scenes.csv'));
  ok = same(`${label} stats CSV`, r.stats, native(file, run, 'stats.csv')) && ok;
  ok = same(`${label} shots JSON`, r.json, native(file, run, 'shots.json')) && ok;
  ok = same(`${label} shots HTML`, r.html, native(file, run, 'shots.html')) && ok;
  if (JSON.stringify(r.shots) !== JSON.stringify(JSON.parse(r.json))) { fail(`${label}: the result's shots aren't its shots JSON`); ok = false; }
  if (r.stills.length !== r.shots.length || r.stills.some(s => !s || s.type !== 'image/jpeg' || s.size < 100)) {
    fail(`${label}: stills ${JSON.stringify(r.stills)} for ${r.shots.length} shots`);
    ok = false;
  }
  if (r.left !== 0) { fail(`${label}: ${r.left} elements left in the page`); ok = false; }
  if (ok) pass(`${label}: ${r.shots.length} shots, the native run's files, a still per shot (threads ${r.threads})`);
}

function noErrors(label, errors) {
  if (errors.length) fail(`${label}: ${errors.join('; ')}`);
}

if (replaced) {
  // Every bundle runs the FastYuv it was packed with.
  const { file, run } = samples[0];
  for (const [label, origin, settings] of [['aot', plain, { bundle: 'aot' }], ['interpreted', plain, { bundle: 'interpreted' }],
    ['threads', isolated, { threads: 2 }]]) {
    const { page } = await open(origin.url);
    const r = await detect(page, file, run, { ...settings, stills: false });
    if (r.error?.includes(MARK)) pass(`replaced FastYuv, ${label}: "${MARK}"`);
    else fail(`replaced FastYuv, ${label}: not the replaced FastYuv: ${r.error ?? '(no exception: the package\'s FastYuv ran)'}`);
    await page.close();
  }
} else {
  // 1. Each sample with each bundle.
  for (const bundle of ['aot', 'interpreted']) {
    const { page, errors } = await open(plain.url);
    for (const { file, run } of samples) compare(`${basename(file)} ${run}, ${bundle}`, await detect(page, file, run, { bundle }), file, run);
    noErrors(bundle, errors);
    await page.close();
  }

  // 2. Threads where the page is isolated; asked for where it isn't, the worker.
  {
    const { file, run } = samples[0];
    const { page, errors } = await open(isolated.url);
    if (!await page.evaluate(() => crossOriginIsolated)) fail('the isolated origin isn\'t crossOriginIsolated');
    const r = await detect(page, file, run, { threads: 2 });
    compare(`${basename(file)} ${run}, threads (isolated)`, r, file, run);
    if (!r.error && r.threads !== 2) fail(`threads (isolated): the run had ${r.threads} threads, not 2`);
    noErrors('threads (isolated)', errors);
    await page.close();
    const other = await open(plain.url);
    const s = await detect(other.page, file, run, { threads: 2 });
    compare(`${basename(file)} ${run}, threads asked for, not isolated`, s, file, run);
    if (!s.error && s.threads !== 1) fail(`threads (not isolated): the run had ${s.threads} threads, not 1`);
    noErrors('threads (not isolated)', other.errors);
    await other.page.close();
  }

  // 3. Stop, pause and resume, cancel, on the AOT bundle and the first sample.
  {
    const { file, run } = samples[0];
    const { page, errors } = await open(plain.url);
    const r = await page.evaluate(async name => {
      const file = new File([await (await fetch(`/samples/${name}`)).blob()], name);
      const out = {};
      const d = new window.sd.ShotDetector();
      const video = await d.open(file);
      // Stopped half-way.
      let stopRun;
      stopRun = d.detect({ onProgress: ({ done }) => { if (done >= video.frames / 2) stopRun.stop(); } });
      const stopped = await stopRun;
      out.stopped = { stopped: stopped.stopped, frames: stopped.frames, stats: await (await d.statsCsv()).text() };
      // Paused at a quarter, no progress while paused, resumed to the end.
      let pausedAt = -1, during = 0, pauseRun;
      pauseRun = d.detect({ onProgress: ({ done }) => {
        if (pausedAt < 0 && done >= video.frames / 4) { pausedAt = done; pauseRun.pause(); setTimeout(() => { during = -1; pauseRun.resume(); }, 1000); } else if (pausedAt >= 0 && during >= 0 && done > pausedAt + 8) during++;
      } });
      const resumed = await pauseRun;
      out.paused = { during, pausedAt, stopped: resumed.stopped, scenes: await d.scenesCsv(), stats: await (await d.statsCsv()).text() };
      // Cancelled through an AbortSignal, then used again.
      const controller = new AbortController();
      try {
        await d.detect({ signal: controller.signal, onProgress: ({ done }) => { if (done >= 8) controller.abort(); } });
        out.cancelled = 'not cancelled';
      } catch (e) {
        out.cancelled = e.name;
      }
      await d.open(file);
      const again = await d.detect();
      out.again = { stopped: again.stopped, scenes: await d.scenesCsv() };
      d.close();
      return out;
    }, basename(file));
    const nativeStats = native(file, run, 'stats.csv').split('\n');
    const stoppedRows = r.stopped.stats.trimEnd().split('\n');
    if (!r.stopped.stopped || r.stopped.frames >= nativeStats.length - 2) fail(`stop: not stopped (${JSON.stringify(r.stopped.frames)} frames)`);
    else if (stoppedRows.some((row, i) => row !== nativeStats[i])) fail('stop: the stats CSV isn\'t the native run\'s rows up to the stop');
    else pass(`stopped at frame ${r.stopped.frames}: the native run's ${stoppedRows.length - 1} stats rows`);
    if (r.paused.pausedAt < 0 || r.paused.during > 0) fail(`pause: progress went on while paused (${JSON.stringify(r.paused)})`);
    else if (r.paused.stopped) fail('pause: the resumed run ended stopped');
    else if (same('paused and resumed, scenes CSV', r.paused.scenes, native(file, run, 'scenes.csv'))
      && same('paused and resumed, stats CSV', r.paused.stats, native(file, run, 'stats.csv'))) pass(`paused at frame ${r.paused.pausedAt} and resumed: the native run's CSVs`);
    if (r.cancelled !== 'AbortError') fail(`cancel: ${r.cancelled}, not an AbortError`);
    else if (r.again.stopped || !same('detected after a cancel, scenes CSV', r.again.scenes, native(file, run, 'scenes.csv'))) fail('detected after a cancel: not the whole run');
    else pass('cancelled with an AbortSignal (AbortError), then detected again: the native run\'s scenes CSV');
    noErrors('stop, pause, cancel', errors);
    await page.close();
  }
}

await browser.close();
plain.server.close();
isolated.server.close();
rmSync(site, { recursive: true, force: true });
console.log(failures.length ? `The package check FAILED: ${failures.length} failures.` : 'The package check passed.');
process.exit(failures.length ? 1 : 0);
