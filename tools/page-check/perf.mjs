// A first visit and a revisit of the page, measured (backlog "A first visit downloads the .NET bundle once, not twice").
// Throwaway: not in the repository. Usage:
//   node perf.mjs <wwwroot> [--runs N] [--delay ms] [--label L]   the bundle served with Pages' headers here
//   node perf.mjs --url <https://...> [--runs N] [--label L]      the live site
// For each run: a fresh profile; the first visit to the page, until its footer's runtime version is filled (the worker's
// runtime answered: interactive); then a revisit in the same profile. The server's own log counts what was fetched
// (local only); for the live site, the requests the browser made of the network (Playwright's events, the service
// worker's included).
import { createServer } from 'node:http';
import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import { extname, join, normalize } from 'node:path';
import { gzipSync } from 'node:zlib';
import { createRequire } from 'node:module';

const require = createRequire(process.env.PW_FROM ?? import.meta.url);
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i < 0 ? fallback : args[i + 1]; };
const runs = Number(opt('--runs', 5)), delay = Number(opt('--delay', 0)), url = opt('--url'), label = opt('--label', '');
const root = url ? null : normalize(args[0]);

const TYPES = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.wasm': 'application/wasm', '.mp4': 'video/mp4', '.dat': 'application/octet-stream',
};
let log = [];
const gz = new Map();
let server, base = url;
if (!url) {
  server = createServer((req, res) => {
    const path = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    const file = join(root, normalize(path.endsWith('/') ? path + 'index.html' : path));
    if (!file.startsWith(root) || !existsSync(file) || !statSync(file).isFile()) return res.writeHead(404).end();
    const s = statSync(file), etag = `"${s.mtimeMs.toString(16)}-${s.size.toString(16)}"`;
    const headers = {
      'Content-Type': TYPES[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'max-age=600', ETag: etag,
      'Last-Modified': s.mtime.toUTCString(),
    };
    const entry = { t: performance.now(), path, status: 200, bytes: 0 };
    log.push(entry);
    const send = () => {
      if (req.headers['if-none-match'] === etag) {
        entry.status = 304;
        return res.writeHead(304, headers).end();
      }
      // Pages gzips what it serves when asked, the .wasm and .dat too (the pass: 4.8 MB gzipped of 14.6 MB).
      if (/gzip/.test(req.headers['accept-encoding'] ?? '') && extname(file) !== '.mp4') {
        if (!gz.has(file)) gz.set(file, gzipSync(readFileSync(file), { level: 6 }));
        const body = gz.get(file);
        entry.bytes = body.length;
        res.writeHead(200, { ...headers, 'Content-Encoding': 'gzip', 'Content-Length': body.length }).end(body);
      } else {
        entry.bytes = s.size;
        res.writeHead(200, { ...headers, 'Content-Length': s.size });
        createReadStream(file).pipe(res);
      }
    };
    delay ? setTimeout(send, delay) : send();
  });
  await new Promise(ok => server.listen(0, '127.0.0.1', ok));
  base = `http://127.0.0.1:${server.address().port}/`;
}

const browser = await chromium.launch(url && process.env.HTTPS_PROXY ? { proxy: { server: process.env.HTTPS_PROXY } } : {});

// Until the footer's runtime slot is filled, across the first visit's reload: ms from the navigation's start, and when
// the page became isolated.
async function visit(page) {
  const start = performance.now();
  let isolatedAt = null, reloads = 0;
  page.on('framenavigated', f => { if (f === page.mainFrame()) reloads++; });
  await page.goto(base, { waitUntil: 'commit' });
  for (;;) {
    try {
      const s = await page.evaluate(() => ({
        iso: globalThis.crossOriginIsolated === true,
        v: document.querySelector('footer .version[data-runtime]')?.textContent?.trim() || '',
      }));
      if (s.iso && isolatedAt === null) isolatedAt = performance.now() - start;
      if (s.v) return { interactive: performance.now() - start, isolated: isolatedAt, documents: reloads };
    } catch { /* the reload replaced the context */ }
    if (performance.now() - start > 60000) throw new Error('not interactive in 60 s');
    await new Promise(r => setTimeout(r, 5));
  }
}

const fw = p => !p.startsWith('(sw)') && /\/_framework\//.test(p) && !/\/threads\//.test(p);
function summary(entries) {
  entries = entries.filter(e => !e.path.startsWith('(sw)'));
  const files = entries.filter(e => fw(e.path));
  const counts = new Map();
  for (const e of files) counts.set(e.path, (counts.get(e.path) ?? 0) + 1);
  const twice = [...counts.values()].filter(n => n > 1).length;
  return {
    requests: entries.length, frameworkRequests: files.length, frameworkFiles: counts.size, fetchedTwiceOrMore: twice,
    ok: files.filter(e => e.status === 200).length, notModified: files.filter(e => e.status === 304).length,
    kb: Math.round(entries.reduce((a, e) => a + e.bytes, 0) / 1024),
    frameworkKb: Math.round(files.reduce((a, e) => a + e.bytes, 0) / 1024),
  };
}

const rows = [];
for (let i = 0; i < runs; i++) {
  const context = await browser.newContext();
  if (url) {
    // The live site: each request that went to the network (the page's, its workers', the service worker's own), not
    // those the service worker answered; an aborted one counts, as started. Bytes: the bodies received (encoded).
    context.on('request', r => {
      const e = { t: performance.now(), path: new URL(r.url()).pathname, status: 0, bytes: 0 };
      log.push(e);
      r.response().then(async resp => {
        if (!resp) return;
        if (resp.fromServiceWorker()) return void (e.path = '(sw) ' + e.path);
        e.status = resp.status();
        const sizes = await r.sizes().catch(() => null);
        e.bytes = Math.max(0, sizes?.responseBodySize ?? 0);
      }).catch(() => { e.status = -1; });
    });
  }
  log = [];
  const page = await context.newPage();
  const first = await visit(page);
  const firstLog = log;
  await page.close();
  log = [];
  const again = await context.newPage();
  const second = await visit(again);
  await new Promise(r => setTimeout(r, 500));
  const secondLog = log;
  await context.close();
  const row = { run: i + 1, first: { ...first, ...summary(firstLog) }, revisit: { ...second, ...summary(secondLog) } };
  rows.push(row);
  console.log(label, JSON.stringify(row));
}
const med = xs => { const s = [...xs].sort((a, b) => a - b); return s[s.length >> 1]; };
console.log(label, 'median first interactive', Math.round(med(rows.map(r => r.first.interactive))),
  'isolated', Math.round(med(rows.map(r => r.first.isolated ?? NaN))),
  'framework requests', med(rows.map(r => r.first.frameworkRequests)), 'twice', med(rows.map(r => r.first.fetchedTwiceOrMore)),
  'KB', med(rows.map(r => r.first.kb)),
  '| revisit interactive', Math.round(med(rows.map(r => r.revisit.interactive))),
  '304s', med(rows.map(r => r.revisit.notModified)), '200s', med(rows.map(r => r.revisit.ok)));
await browser.close();
server?.close();
