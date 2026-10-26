// WebCodecs against ManagedCodecs (README, "WebCodecs"): serves check.html and the data tools/WebCodecsCheck prepared,
// and writes what any browser opened on it reports. Not part of the checks or of CI.
//
//   node tools/webcodecs/serve.mjs --data out/webcodecs/data --results out/webcodecs/results
//     [--wasm tools/WebCodecsWasm/bin/Release/net10.0/publish/wwwroot]   ManagedCodecs on wasm, for the timings
//     [--reference tools/WebCodecsCheck/bin/Release/net10.0/WebCodecsCheck.dll --samples samples]   where a frame differs
//     [--port 8090]
//   node tools/webcodecs/serve.mjs --results out/webcodecs/results --report    the tables, from the results so far
//
// Then open http://localhost:8090/check.html in a browser (WebCodecs needs a secure context: localhost is one), with
// ?compare=all (or names) and ?time=names (see check.js). Each pass's result goes to <results>/<browser>/<pass>.json,
// a frame that differs to <results>/<browser>/<pass>.yuv, and the tables to <results>/report.md when the page is done.
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { basename, dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const options = { port: 8090 };
for (let i = 2; i < process.argv.length; i++) {
  const arg = process.argv[i];
  if (arg === '--report') options.report = true;
  else if (arg.startsWith('--')) options[arg.slice(2)] = process.argv[++i];
  else throw new Error(`Unknown argument: ${arg}`);
}
if (!options.results) throw new Error('Give --results <dir>.');

if (options.report) {
  console.log(report(options.results));
  process.exit(0);
}
if (!options.data) throw new Error('Give --data <dir> (WebCodecsCheck prepare --out).');

const here = dirname(fileURLToPath(import.meta.url));
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.wasm': 'application/wasm', '.dat': 'application/octet-stream' };

// A file under root, or null for a path that leaves it.
function under(root, path) {
  const file = resolve(root, '.' + decodeURIComponent(path));
  return file.startsWith(resolve(root) + sep) && existsSync(file) && statSync(file).isFile() ? file : null;
}

function send(res, file) {
  res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream', 'content-length': statSync(file).size,
    'cache-control': 'no-store' });
  createReadStream(file).pipe(res);
}

async function body(req) {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  return Buffer.concat(chunks);
}

// A browser's name and version, as a folder name: "Chrome-153.0.8010.12", "Firefox-136.0".
const folder = name => join(options.results, name.replace(/[^\w.-]+/g, '-'));
const passName = name => name.replace(/[^\w.-]+/g, '-');

createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    let file;
    if (req.method === 'GET' && url.pathname === '/plan') {
      const samples = readdirSync(options.data).filter(f => f.endsWith('.json')).map(f => basename(f, '.json')).sort();
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ samples, wasm: !!options.wasm }));
    } else if (req.method === 'POST' && url.pathname === '/result') {
      const dir = folder(url.searchParams.get('browser'));
      mkdirSync(dir, { recursive: true });
      writeFileSync(join(dir, passName(url.searchParams.get('pass')) + '.json'), await body(req));
      res.end('ok');
    } else if (req.method === 'POST' && url.pathname === '/frame') {
      // The first frame that differs, as the browser copied it out: where and by how much, from the reference tool.
      const dir = folder(url.searchParams.get('browser'));
      mkdirSync(dir, { recursive: true });
      const yuv = join(dir, passName(url.searchParams.get('pass')) + '.yuv');
      writeFileSync(yuv, await body(req));
      if (!options.reference) {
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end(JSON.stringify({ unexplained: 'serve.mjs was started without --reference' }));
        return;
      }
      const meta = JSON.parse(readFileSync(join(options.data, url.searchParams.get('sample') + '.json'), 'utf8'));
      const out = execFileSync('dotnet', [options.reference, 'diff', join(options.samples ?? 'samples', meta.file),
        url.searchParams.get('frame'), yuv], { encoding: 'utf8', maxBuffer: 1 << 26 });
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(out);
    } else if (req.method === 'POST' && url.pathname === '/done') {
      const text = report(options.results);
      writeFileSync(join(options.results, 'report.md'), text);
      console.log(text);
      res.end('ok');
    } else if (req.method === 'POST' && url.pathname === '/log') {
      console.log((await body(req)).toString());
      res.end('ok');
    } else if (req.method === 'GET' && url.pathname.startsWith('/data/') && (file = under(options.data, url.pathname.slice(5)))) {
      send(res, file);
    } else if (req.method === 'GET' && options.wasm && url.pathname.startsWith('/wasm/') && (file = under(options.wasm, url.pathname.slice(5)))) {
      send(res, file);
    } else if (req.method === 'GET' && (file = under(here, url.pathname === '/' ? '/check.html' : url.pathname))) {
      send(res, file);
    } else {
      console.log('404', req.url);
      res.writeHead(404);
      res.end();
    }
  } catch (e) {
    console.error(e);
    res.writeHead(500);
    res.end(String(e));
  }
}).listen(options.port, 'localhost', () => console.log(`http://localhost:${options.port}/check.html`));

// The tables: per browser, one row per sample and pass, then the timings.
function report(results) {
  const lines = [];
  const browsers = existsSync(results) ? readdirSync(results).filter(d => statSync(join(results, d)).isDirectory()).sort() : [];
  for (const browser of browsers) {
    const passes = readdirSync(join(results, browser)).filter(f => f.endsWith('.json'))
      .map(f => JSON.parse(readFileSync(join(results, browser, f), 'utf8')));
    const compares = passes.filter(p => p.kind === 'compare').sort((a, b) => a.sample.localeCompare(b.sample) || a.accel.localeCompare(b.accel));
    const first = passes.find(p => p.support);
    lines.push(`### ${browser}`, '');
    if (first) {
      lines.push(`isConfigSupported (${first.support.codec}): ` +
        Object.entries(first.support.answers).map(([accel, ok]) => `${accel} ${ok}`).join(', ') + '.', '');
    }
    if (compares.length) {
      lines.push('| Sample | Asked for | Frames | Frame as decoded | Result |', '| --- | --- | --- | --- | --- |');
      for (const p of compares) lines.push(`| ${p.sample} | ${p.accel} | ${p.frames} of ${p.expected} | ${p.layout ?? '-'} | ${verdict(p)} |`);
      lines.push('');
    }
    const timings = passes.filter(p => p.kind === 'time').sort((a, b) => a.sample.localeCompare(b.sample) || a.decoder.localeCompare(b.decoder));
    if (timings.length) {
      lines.push('| Sample | Decoder | Copied out | Frames | Runs (s) | Median | Frames per second |', '| --- | --- | --- | --- | --- | --- | --- |');
      for (const p of timings) {
        if (!p.seconds.length) {
          lines.push(`| ${p.sample} | ${p.decoder} | - | - | - | - | error: ${p.error} |`);
          continue;
        }
        const median = [...p.seconds].sort((a, b) => a - b)[p.seconds.length >> 1];
        lines.push(`| ${p.sample} | ${p.decoder} | ${p.layout?.split(',')[0] ?? '-'} | ${p.frames} | ${p.seconds.map(s => s.toFixed(2)).join(', ')} | ${median.toFixed(2)} s | ${(p.frames / median).toFixed(0)} |`);
      }
      lines.push('');
    }
  }
  return lines.join('\n');
}

function verdict(p) {
  if (p.error) return `error: ${p.error}`;
  if (p.unsupported) return 'not supported';
  if (p.notYuv) return `differs: the frames come out in RGB (${p.layout.split(' ')[0]}), converted by the browser, so there are no Y, U and V planes to compare`;
  const problems = [];
  if (p.frames !== p.expected) problems.push(`${p.frames} frames, not ${p.expected}`);
  if (p.ptsDiffer) problems.push(`${p.ptsDiffer} timestamps differ (first: frame ${p.firstPts.frame}, ${p.firstPts.got} for ${p.firstPts.want})`);
  if (p.sizeDiffers) problems.push(`visible ${p.sizeDiffers}`);
  if (p.differing) {
    const planes = Object.entries(p.differing).filter(([, n]) => n).map(([plane, n]) => `${plane} in ${n}`).join(', ');
    let where = `frames differ: ${planes}`;
    const d = p.firstDiff;
    if (d?.planes) {
      const plane = d.planes.find(x => x.differing);
      where += `; first frame ${d.frame} (pts ${d.pts}), ` + d.planes.filter(x => x.differing)
        .map(x => `${x.plane}: ${x.differing} samples, up to ${x.largest} apart`).join(', ') +
        (plane ? `, first at (${plane.x}, ${plane.y}) in ${plane.plane}: ${plane.managedCodecs} vs ${plane.webCodecs}` : '');
    } else if (d) {
      where += `; first frame ${d.frame}`;
    }
    problems.push(where);
  }
  return problems.length ? problems.join('; ') : '**every frame identical**';
}
