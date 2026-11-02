// The contact sheet, a card under the run's (issue #93): a grid of the run's shots, each with its still or three (its
// first, middle and last frame, as scenedetect's --num-images 3), its number, start timecode and length, in as many
// columns as chosen, under a header with the file's name, its duration, the shot count and the average shot length;
// downloaded as a PNG and as a PDF to print on A4 or Letter. Made when asked (a still is a seek), again when an option
// changes, the stills kept. The stills are the browser's decode, as the shots table's, through shots.js' grabber (the
// hidden <video>, or an AVI's own JPEG through app.js' frameAt): a picture of the video, not ShotDetector's frame, so the
// card is marked the page's. The exact sheet, from the decoded frames, is ShotDetector CLI's (shotdetector#649).
//
// The PDF is written here, by hand (the page has no dependency and no build step): PDF 1.4, the stills as JPEGs
// (DCTDecode, the canvas's own encoding), the text in Helvetica, one of PDF's standard fonts, so nothing is embedded but
// the pictures. Not print CSS and window.print(): that gives no file to keep or send, its page size and margins are the
// browser's dialog's, and the page check couldn't download it.
import { card, frameSeconds, on } from '../app.js';
import { timecode } from '../timeline.js';
import { stillAt, stillTimes } from './shots.js';

const section = card('Contact sheet', {
  place: 'run',
  mark: 'page',
  markTitle: 'Made on the page from this browser\'s decode of the video, the stills the shots table\'s: pictures of the ' +
    'shots, not ShotDetector\'s frames. The numbers, timecodes and lengths are the run\'s shots',
});
section.id = 'sheet';
section.hidden = true;
section.insertAdjacentHTML('beforeend', `
  <p class="sheet-options">
    <label>Stills a shot <select id="sheet-stills"><option value="1">1</option><option value="3">3</option></select></label>
    <label>Columns <input id="sheet-columns" type="number" min="1" max="8" step="1" value="4"></label>
    <label>Paper <select id="sheet-paper"><option value="a4">A4</option><option value="letter">Letter</option></select></label>
    <label><input id="sheet-header" type="checkbox" checked> Header</label>
  </p>
  <p class="downloads">
    <button id="sheet-make" type="button">Make the sheet</button>
    <a id="sheet-png" download hidden>PNG</a>
    <a id="sheet-pdf" download hidden>PDF</a>
    <span id="sheet-status"></span>
  </p>`);
const $ = id => section.querySelector(`#${id}`);
const stillsField = $('sheet-stills'), columnsField = $('sheet-columns'), paperField = $('sheet-paper');
const headerField = $('sheet-header'), make = $('sheet-make'), pngLink = $('sheet-png'), pdfLink = $('sheet-pdf');
const status = $('sheet-status');
make.title = 'Takes the stills (a seek each, kept for the next sheet) and makes the sheet as a PNG and a PDF';

const STILL_PIXELS = { 1: 480, 3: 320 }; // a still's width as taken, by stills a shot: about 250 dpi on paper
const PAPER = { a4: [595.28, 841.89], letter: [612, 792] }; // in points
let run = null; // the run shown: { shots, name, file, duration, stopped }
let runs = 0, made = false;
let making = false, again = false; // a sheet being made, and an option changed meanwhile: made again once it's done
const stills = new Map(); // `${time} ${width}` -> the canvas, or null when the browser didn't show it

function clear() {
  runs++;
  run = null;
  made = false;
  section.hidden = true;
  for (const a of [pngLink, pdfLink]) {
    if (a.href) URL.revokeObjectURL(a.href);
    a.removeAttribute('href');
    a.hidden = true;
  }
  status.textContent = '';
  make.disabled = false;
}

let fileName = '';
on('chose', ({ file }) => {
  fileName = file.name;
  stills.clear();
  clear();
});
on('run', clear);
on('cancelled', clear);
on('done', ({ video, result: r, name }) => {
  clear();
  if (r.shots.length === 0) return;
  run = { shots: r.shots, name, file: fileName, stopped: r.stopped,
    duration: !r.stopped && r.duration != null ? r.duration : frameSeconds(video, r.frameCount) };
  section.hidden = false;
});

make.addEventListener('click', () => sheet());
for (const field of [stillsField, columnsField, paperField, headerField])
  field.addEventListener('change', () => { if (made) sheet(); });

const options = () => ({
  stills: Number(stillsField.value),
  columns: Math.min(8, Math.max(1, Math.round(Number(columnsField.value)) || 4)),
  paper: paperField.value,
  header: headerField.checked,
});

// The stills, then the PNG and the PDF; one sheet at a time, the last options winning.
async function sheet() {
  if (making) { again = true; return; }
  making = true;
  try {
    do {
      again = false;
      await makeSheet();
    } while (again);
  } finally {
    making = false;
  }
}

async function makeSheet() {
  if (!run) return;
  const at = runs, o = options(), width = STILL_PIXELS[o.stills];
  make.disabled = true;
  const wanted = run.shots.flatMap(shot => stillTimes(shot, o.stills).map(t => `${t} ${width}`));
  let taken = 0;
  for (const key of wanted) {
    if (!stills.has(key)) {
      status.textContent = `Taking the stills: ${taken + 1} of ${wanted.length}…`;
      const [t] = key.split(' ');
      stills.set(key, await stillAt(Number(t), width));
      if (at !== runs) return;
    }
    taken++;
  }
  status.textContent = 'Making the sheet…';
  const cells = run.shots.map(shot => ({
    shot,
    pictures: stillTimes(shot, o.stills).map(t => stills.get(`${t} ${width}`)),
  }));
  const shown = cells.reduce((n, c) => n + c.pictures.filter(Boolean).length, 0);
  const [png, pdf] = await Promise.all([pngSheet(cells, o), pdfSheet(cells, o)]);
  if (at !== runs) return;
  const what = `${run.shots.length} ${run.shots.length === 1 ? 'shot' : 'shots'}, ${shown} stills of this browser's ` +
    `decode${run.stopped ? ', for the part read' : ''}`;
  link(pngLink, png.blob, `${run.name}.sheet.png`, `The contact sheet as a picture, ${png.width}×${png.height}: ${what}`);
  link(pdfLink, pdf.blob, `${run.name}.sheet.${o.paper}.pdf`,
    `The contact sheet to print, ${pdf.pages} ${pdf.pages === 1 ? 'page' : 'pages'} of ${o.paper === 'a4' ? 'A4' : 'Letter'}: ${what}`);
  for (const a of [pngLink, pdfLink]) a.dataset.stills = shown;
  const missing = cells.length * o.stills - shown;
  status.textContent = missing > 0 ? `${missing} ${missing === 1 ? 'still' : 'stills'} this browser didn't show` : '';
  made = true;
  make.disabled = false;
}

function link(a, blob, name, title) {
  if (a.href) URL.revokeObjectURL(a.href);
  a.href = URL.createObjectURL(blob);
  a.download = name;
  a.title = `${name}: ${title}`;
  a.hidden = false;
}

// The sheet's words.
const length = s => `${s.toFixed(3)} s`;
const headerLines = () => {
  const n = run.shots.length, mean = run.shots.reduce((sum, s) => sum + s.durationSeconds, 0) / n;
  return [run.file, `${run.stopped ? 'Read to' : 'Duration'} ${timecode(run.duration)} · ${n} ${n === 1 ? 'shot' : 'shots'}` +
    ` · average shot length ${length(mean)}${run.stopped ? ' · the run stopped there' : ''}`];
};
const label = shot => `#${shot.shot}   ${shot.startTimecode}   ${length(shot.durationSeconds)}`;
const FOOTER = 'Stills: this browser\'s decode, not ShotDetector\'s frames. Made by shotdetector-web.';
// The stills' aspect: the first still taken's (the picture as shown, rotated as the browser shows it), else 16:9.
const aspect = cells => {
  const c = cells.flatMap(c => c.pictures).find(Boolean);
  return c ? c.height / c.width : 9 / 16;
};

// The grid, in a unit of its own: each cell its stills side by side, then a line under them.
function grid(cells, o, cellWidth, gap, lineHeight) {
  const still = (cellWidth - (o.stills - 1) * gap / 3) / o.stills;
  return { still, stillHeight: still * aspect(cells), rowHeight: still * aspect(cells) + lineHeight + gap };
}

// The PNG: the whole sheet on one canvas, white, 1 or 3 stills of 320 or 240 pixels a shot; scaled down to stay a size
// browsers make (32,767 pixels a side, about 100 megapixels).
async function pngSheet(cells, o) {
  const stillWidth = o.stills === 1 ? 320 : 240, gap = 16, margin = 32, line = 22;
  const cellWidth = stillWidth * o.stills + (o.stills - 1) * gap / 3;
  const g = grid(cells, o, cellWidth, gap, line);
  const header = o.header ? 76 : 0, rows = Math.ceil(cells.length / o.columns);
  const width = 2 * margin + o.columns * cellWidth + (o.columns - 1) * gap;
  const height = 2 * margin + header + rows * g.rowHeight + 20;
  const scale = Math.min(1, 32000 / width, 32000 / height, Math.sqrt(100e6 / (width * height)));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(width * scale);
  canvas.height = Math.round(height * scale);
  const ctx = canvas.getContext('2d');
  ctx.scale(scale, scale);
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, width, height);
  ctx.fillStyle = '#111';
  ctx.textBaseline = 'alphabetic';
  if (o.header) {
    const [title, facts] = headerLines();
    ctx.font = 'bold 26px Helvetica, Arial, sans-serif';
    ctx.fillText(title, margin, margin + 26, width - 2 * margin);
    ctx.font = '17px Helvetica, Arial, sans-serif';
    ctx.fillText(facts, margin, margin + 54, width - 2 * margin);
  }
  ctx.font = '15px Helvetica, Arial, sans-serif';
  cells.forEach((c, i) => {
    const x = margin + (i % o.columns) * (cellWidth + gap), y = margin + header + Math.floor(i / o.columns) * g.rowHeight;
    c.pictures.forEach((p, k) => {
      const px = x + k * (g.still + gap / 3);
      if (p) ctx.drawImage(p, px, y, g.still, g.stillHeight);
      else missingStill(ctx, px, y, g.still, g.stillHeight);
    });
    ctx.fillStyle = '#111';
    ctx.fillText(label(c.shot), x, y + g.stillHeight + 17, cellWidth);
  });
  ctx.fillStyle = '#666';
  ctx.font = '12px Helvetica, Arial, sans-serif';
  ctx.fillText(FOOTER, margin, height - margin + 4, width - 2 * margin);
  const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
  return { blob, width: canvas.width, height: canvas.height };
}

function missingStill(ctx, x, y, w, h) {
  ctx.fillStyle = '#ddd';
  ctx.fillRect(x, y, w, h);
  ctx.fillStyle = '#666';
  ctx.fillText('no still', x + 8, y + h / 2);
}

// The PDF: pages of the paper chosen, half-inch margins, the header on the first, a footer and the page's number on
// each.
async function pdfSheet(cells, o) {
  const [W, H] = PAPER[o.paper], margin = 36, gap = 10, line = 11, footer = 18;
  const cellWidth = (W - 2 * margin - (o.columns - 1) * gap) / o.columns;
  const g = grid(cells, o, cellWidth, gap, line);
  const header = o.header ? 40 : 0;
  const fits = top => Math.max(1, Math.floor((H - 2 * margin - footer - top + gap) / g.rowHeight));
  // The JPEGs, one per still shown, each an image object.
  const jpegs = new Map();
  for (const p of cells.flatMap(c => c.pictures)) {
    if (p && !jpegs.has(p)) jpegs.set(p, { bytes: new Uint8Array(await (await jpeg(p)).arrayBuffer()), w: p.width, h: p.height });
  }
  const images = [...jpegs.values()];
  images.forEach((im, i) => { im.name = `Im${i + 1}`; });
  // The pages' content streams.
  const pages = [];
  let i = 0;
  while (i < cells.length || pages.length === 0) {
    const first = pages.length === 0, top = first ? header : 0, ops = [];
    const font = (bold, size) => `/${bold ? 'F2' : 'F1'} ${size} Tf`;
    // Helvetica's characters average about half the font's size wide: a line longer than its width is cut.
    const text = (s, x, y, size, bold = false, width = W - margin - x) =>
      ops.push(`0.07 g BT ${font(bold, size)} ${n(x)} ${n(y)} Td (${pdfText(s, width / (size * 0.55))}) Tj ET`);
    if (first && o.header) {
      const [title, facts] = headerLines();
      text(title, margin, H - margin - 14, 14, true);
      text(facts, margin, H - margin - 30, 9);
    }
    const perPage = fits(top) * o.columns;
    cells.slice(i, i + perPage).forEach((c, k) => {
      const x = margin + (k % o.columns) * (cellWidth + gap);
      const y = H - margin - top - Math.floor(k / o.columns) * g.rowHeight - g.stillHeight;
      c.pictures.forEach((p, j) => {
        const px = x + j * (g.still + gap / 3);
        if (p) ops.push(`q ${n(g.still)} 0 0 ${n(g.stillHeight)} ${n(px)} ${n(y)} cm /${jpegs.get(p).name} Do Q`);
        else ops.push(`0.87 g ${n(px)} ${n(y)} ${n(g.still)} ${n(g.stillHeight)} re f`);
      });
      // The line under the stills, smaller in narrow columns so that it fits.
      const words = label(c.shot);
      text(words, x, y - 8, Math.min(7, cellWidth / (words.length * 0.5)), false, cellWidth);
    });
    i += perPage;
    pages.push(ops);
  }
  pages.forEach((ops, p) => {
    ops.push(`0.4 g BT /F1 6.5 Tf ${n(margin)} ${n(margin - 4)} Td (${pdfText(FOOTER, 200)}) Tj ET`);
    const number = `${p + 1} / ${pages.length}`;
    ops.push(`0.4 g BT /F1 6.5 Tf ${n(W - margin - number.length * 3.6)} ${n(margin - 4)} Td (${number}) Tj ET`);
  });
  return { blob: pdfFile(pages, images, W, H, `${run.file}: contact sheet`), pages: pages.length };
}

const jpeg = canvas => new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.88));
const n = v => Number(v.toFixed(2)).toString();

// A string for a PDF literal: Helvetica's WinAnsi characters (Latin-1's printable ones), others as '?', cut to a
// number of characters with "...", and \, ( and ) escaped.
function pdfText(s, most) {
  let t = [...s].map(ch => { const c = ch.codePointAt(0); return (c >= 32 && c < 127) || (c >= 160 && c < 256) ? ch : '?'; }).join('');
  if (t.length > most) t = `${t.slice(0, Math.max(0, Math.floor(most) - 3))}...`;
  return t.replace(/[\\()]/g, ch => `\\${ch}`);
}

// The file: the catalog, the page tree, the two fonts, the images, each page and its content, the cross-reference
// table at the byte offsets they were written at, and the trailer.
function pdfFile(pages, images, W, H, title) {
  const parts = [], offsets = [];
  let size = 0;
  const bytes = s => Uint8Array.from(s, ch => ch.charCodeAt(0)); // every character below 256: Latin-1 bytes
  const put = part => { const b = typeof part === 'string' ? bytes(part) : part; parts.push(b); size += b.length; };
  const object = (id, ...body) => { offsets[id] = size; put(`${id} 0 obj\n`); body.forEach(put); put('\nendobj\n'); };
  const firstImage = 6, firstPage = firstImage + images.length;
  put('%PDF-1.4\n%\xe2\xe3\xcf\xd3\n');
  object(1, '<< /Type /Catalog /Pages 2 0 R >>');
  object(2, `<< /Type /Pages /Count ${pages.length} /Kids [${pages.map((_, p) => `${firstPage + 2 * p} 0 R`).join(' ')}] >>`);
  object(3, '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
  object(4, '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>');
  object(5, `<< /Title (${pdfText(title, 200)}) /Producer (shotdetector-web) >>`);
  images.forEach((im, k) => object(firstImage + k, `<< /Type /XObject /Subtype /Image /Width ${im.w} /Height ${im.h} ` +
    `/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${im.bytes.length} >>\nstream\n`, im.bytes,
    '\nendstream'));
  const xobjects = images.map((im, k) => `/${im.name} ${firstImage + k} 0 R`).join(' ');
  pages.forEach((ops, p) => {
    const id = firstPage + 2 * p, content = bytes(ops.join('\n'));
    object(id, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${n(W)} ${n(H)}] /Contents ${id + 1} 0 R ` +
      `/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << ${xobjects} >> >> >>`);
    object(id + 1, `<< /Length ${content.length} >>\nstream\n`, content, '\nendstream');
  });
  const xref = size, count = firstPage + 2 * pages.length;
  put(`xref\n0 ${count}\n0000000000 65535 f \n`);
  for (let id = 1; id < count; id++) put(`${String(offsets[id]).padStart(10, '0')} 00000 n \n`);
  put(`trailer\n<< /Size ${count} /Root 1 0 R /Info 5 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
  return new Blob(parts, { type: 'application/pdf' });
}
