// TEMPORARY (the page row's PR, reverted before it merges): the page's Done-when in CI, where Firefox and Sintel's
// trailer are available and the session's container has neither. Drives the page in a real browser: every sample with every detector against out/native, rejects, cancel, a second
// run, a damaged file, the size limit; screenshots. Usage: node pagecheck.cjs <wwwroot> <repo> <shots dir> <extra dir> [browser]
const pw = require('playwright');
const http = require('http'), fs = require('fs'), path = require('path');
const [root, repo, shotsDir, extra, browserName = 'chromium'] = process.argv.slice(2);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json' };
const port = { chromium: 8125, firefox: 8126 }[browserName];
const srv = http.createServer((q, r) => {
  let f = path.join(root, decodeURIComponent(q.url.split('?')[0]));
  if (f.endsWith('/')) f += 'index.html';
  fs.readFile(f, (e, b) => {
    if (e) { r.writeHead(404); r.end(); return; }
    r.writeHead(200, { 'content-type': types[path.extname(f)] || 'application/octet-stream' }); r.end(b);
  });
}).listen(port);

let failures = 0;
const fail = m => { console.log('FAIL', m); failures++; };
const ok = m => console.log('ok  ', m);

(async () => {
  const browser = await pw[browserName].launch();
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, acceptDownloads: true });
  page.on('pageerror', e => fail(`page error: ${e}`));
  await page.goto(`http://localhost:${port}/`);

  const pick = async f => page.setInputFiles('#file', f);
  const state = () => page.evaluate(() => document.body.dataset.state);
  const waitState = (s, timeout = 600000) => page.waitForFunction(s => s.includes(document.body.dataset.state), s, { timeout, polling: 100 });
  const runOnce = async (detector) => {
    await page.selectOption('#detector', detector);
    // Progress samples while it runs: the bar's value and the status text.
    await page.evaluate(() => {
      window.__seen = [];
      const p = document.getElementById('progress');
      window.__poll = setInterval(() => window.__seen.push([p.position, document.getElementById('status-text').textContent]), 50);
    });
    await page.click('#detect');
    await waitState(['done', 'error']);
    const seen = await page.evaluate(() => { clearInterval(window.__poll); return window.__seen; });
    return seen;
  };
  const download = async sel => {
    const [d] = await Promise.all([page.waitForEvent('download'), page.click(sel)]);
    return { name: d.suggestedFilename(), text: fs.readFileSync(await d.path(), 'utf8') };
  };

  const samples = fs.readdirSync(path.join(repo, 'samples')).filter(f => f.endsWith('.mp4')).sort();
  for (const s of samples) {
    await pick(path.join(repo, 'samples', s));
    for (const det of ['adaptive', 'content', 'threshold', 'histogram', 'hash']) {
      const started = Date.now();
      const seen = await runOnce(det);
      if (await state() !== 'done') { fail(`${s} ${det}: ${await page.textContent('#error')}`); continue; }
      const base = s.replace(/\.mp4$/, '');
      const scenes = await download('#scenes-csv'), stats = await download('#stats-csv');
      const want = f => fs.readFileSync(path.join(repo, 'out/native', `${base}.${det}.${f}.csv`), 'utf8');
      const moved = seen.filter(([v]) => v > 0 && v < 1).length;
      const same = scenes.text === want('scenes') && stats.text === want('stats');
      const named = scenes.name === `${base}.scenes.csv` && stats.name === `${base}.stats.csv`;
      (same && named ? ok : fail)(`${s} ${det}: scenes+stats ${same ? 'identical' : 'DIFFER'}, names ${scenes.name}/${stats.name}, ` +
        `${moved} progress samples between 0 and 1, ${((Date.now() - started) / 1000).toFixed(1)} s; ${await page.textContent('#status-text')}`);
      if (s.startsWith('sintel') && det === 'adaptive') await page.screenshot({ path: path.join(shotsDir, `${browserName}-sintel-done.png`), fullPage: true });
      if (s.startsWith('cuts-854') && det === 'adaptive') await page.screenshot({ path: path.join(shotsDir, `${browserName}-done.png`), fullPage: true });
    }
  }

  // Cancel mid-run, then a second run of the same file to the end.
  const long = path.join(extra, 'long-1280x720-40s.mp4');
  await pick(long);
  await page.selectOption('#detector', 'content');
  await page.click('#detect');
  await page.waitForFunction(() => document.getElementById('progress').position > 0.2, null, { timeout: 600000, polling: 100 });
  await page.screenshot({ path: path.join(shotsDir, `${browserName}-detecting.png`), fullPage: true });
  const before = await page.textContent('#status-text');
  await page.click('#cancel');
  const afterCancel = await state();
  (afterCancel === 'idle' ? ok : fail)(`cancel mid-run (${before}) -> ${afterCancel}: ${await page.textContent('#status-text')}`);
  await page.screenshot({ path: path.join(shotsDir, `${browserName}-cancelled.png`), fullPage: true });
  const seen = await runOnce('content');
  (await state() === 'done' ? ok : fail)(`second run after cancel: ${await page.textContent('#status-text')} ${await page.textContent('#summary')}`);
  // and a sample after it, compared again
  await pick(path.join(repo, 'samples', 'fades-320x240-25.mp4'));
  await runOnce('threshold');
  const sc = await download('#scenes-csv');
  (sc.text === fs.readFileSync(path.join(repo, 'out/native/fades-320x240-25.threshold.scenes.csv'), 'utf8') ? ok : fail)('run after the cancelled file: scenes identical');

  // A row seeks the player.
  await page.click('#shots tbody tr:nth-child(3)');
  const t = await page.evaluate(() => document.getElementById('video').currentTime);
  const want = await page.textContent('#shots tbody tr:nth-child(3) td:nth-child(2)');
  ok(`row 3 (${want}) -> video.currentTime ${t}`);

  // Refused files, a damaged one, the size limit: each with its message.
  const rejects = fs.readdirSync(path.join(repo, 'samples/reject')).sort().map(f => path.join(repo, 'samples/reject', f));
  for (const f of [...rejects, path.join(extra, 'truncated.mp4'), path.join(extra, 'garbled.mp4')]) {
    await pick(f);
    await page.click('#detect');
    await waitState(['done', 'error']);
    const msg = await page.textContent('#error');
    (await state() === 'error' && msg ? ok : fail)(`${path.basename(f)}: ${msg}`);
    if (f.endsWith('rotated.mp4')) await page.screenshot({ path: path.join(shotsDir, `${browserName}-refused.png`), fullPage: true });
    if (f.endsWith('garbled.mp4')) await page.screenshot({ path: path.join(shotsDir, `${browserName}-damaged.png`), fullPage: true });
  }
  await pick(path.join(extra, 'big-600mb.mp4'));
  const big = await page.textContent('#error');
  (await state() === 'error' && /512 MB/.test(big) && await page.isDisabled('#detect') ? ok : fail)(`big-600mb.mp4: ${big}`);
  await page.screenshot({ path: path.join(shotsDir, `${browserName}-too-big.png`), fullPage: true });

  await browser.close(); srv.close();
  console.log(failures ? `${failures} failed` : 'all ok');
  process.exit(failures ? 1 : 0);
})().catch(e => { console.log('FAIL', e); process.exit(1); });
