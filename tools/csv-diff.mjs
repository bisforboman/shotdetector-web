// Where two CSVs that should be the same differ: the first rows that differ, each with its frame number, the columns
// that differ in it (the header's name) and the two values. tools/realworld.sh (its "Differs:" loop, the AOT bundle's
// CSVs against the native ones) and tools/app-check.mjs (a run paused, split, stopped or resumed against the whole run)
// print them to the log and, under GitHub Actions, as an ::error annotation per file: GitHub keeps 10 a step, so one
// for a file, its rows in it.
//   node tools/csv-diff.mjs <expected.csv> <actual.csv> [<expected's label> <actual's label>]
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// How many differing rows are told, and how many differing columns of each.
const ROWS = 3, COLUMNS = 4;

const lines = csv => csv.split(/\r?\n/).filter(line => line !== '');

// The rows (line indexes) where a and b differ, from the first, at most max; `only` limits them to those lines.
// scenedetect's scenes CSV starts with its "Timecode List:" line, its header the next one.
export function differingRows(a, b, { max = ROWS, only = null } = {}) {
  const ours = lines(a), theirs = lines(b);
  const at = ours[0]?.startsWith('Timecode List') ? 1 : 0;
  const header = (ours[at] ?? theirs[at] ?? '').split(',');
  // A row's frame: its "Frame Number" (the stats CSV) or "Start Frame" (the scenes CSV), else its first cell.
  const frameColumn = Math.max(header.indexOf('Frame Number'), header.indexOf('Start Frame'), 0);
  const indexes = only ?? Array.from({ length: Math.max(ours.length, theirs.length) }, (_, i) => i);
  const differing = indexes.filter(i => ours[i] !== theirs[i]);
  const rows = differing.slice(0, max).map(i => {
    const x = ours[i]?.split(','), y = theirs[i]?.split(',');
    const frame = (x ?? y)[frameColumn];
    if (!x || !y || i <= at) return { line: i + 1, frame, columns: [], a: ours[i] ?? null, b: theirs[i] ?? null };
    const columns = [];
    for (let c = 0; c < Math.max(x.length, y.length); c++)
      if (x[c] !== y[c]) columns.push({ column: header[c] ?? `column ${c + 1}`, a: x[c] ?? null, b: y[c] ?? null });
    return { line: i + 1, frame, columns };
  });
  return { rows, total: differing.length, frameName: header[frameColumn] ?? 'row' };
}

// The rows as lines of text: "frame 1234 (line 1236): content_val (w=2) native 12.345, wasm 12.346".
export function describe(diff, [aLabel, bLabel] = ['expected', 'actual']) {
  const value = v => v === null ? '(none)' : JSON.stringify(v);
  const out = diff.rows.map(r => {
    const where = `${r.frameName ?? diff.frameName} ${r.frame} (line ${r.line})`;
    if (r.columns.length === 0) return `${where}: ${aLabel} ${value(r.a)}, ${bLabel} ${value(r.b)}`;
    const shown = r.columns.slice(0, COLUMNS).map(c => `"${c.column}" ${aLabel} ${value(c.a)}, ${bLabel} ${value(c.b)}`);
    const more = r.columns.length > COLUMNS ? `; and ${r.columns.length - COLUMNS} more columns` : '';
    return `${where}: ${shown.join('; ')}${more}`;
  });
  if (diff.total > diff.rows.length) out.push(`... ${diff.total - diff.rows.length} more differing rows (${diff.total} in all)`);
  return out;
}

// The differing rows of `file` printed (to stderr, as app-check.mjs's notes, or stdout) and, under GitHub Actions, as
// one ::error annotation on it. Returns the lines.
export function report(file, a, b, labels, { only = null, print = console.error } = {}) {
  const diff = differingRows(a, b, { only });
  if (diff.total === 0) return [];
  const text = describe(diff, labels);
  for (const line of text) print(`  ${file}: ${line}`);
  if (process.env.GITHUB_ACTIONS === 'true') {
    const escape = s => s.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
    const property = s => escape(s).replace(/:/g, '%3A').replace(/,/g, '%2C');
    print(`::error file=${property(file)}::${escape(`${file} differs (${diff.total} rows):\n${text.join('\n')}`)}`);
  }
  return text;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const [expected, actual, aLabel = 'expected', bLabel = 'actual'] = process.argv.slice(2);
  report(actual, readFileSync(expected, 'utf8'), readFileSync(actual, 'utf8'), [aLabel, bLabel], { print: console.log });
}
