// About this file, a card: what the core says of the file once it is opened (Api's Open: the YuvVideo handed to
// ShotDetector, which NativeCheck checks field by field against FFmpeg's, and the audio track as ffprobe describes it),
// in the words a viewer knows, each field's own value in its tooltip and Open's JSON one click away. Nothing here is
// the page's own reading: only the core's fields, named.
import { card, on } from '../app.js';

// FFmpeg's names of the matrix coefficients (Mp4Input's MatrixNames; an AVI's MJPEG is always bt470bg), as a viewer knows them; "" is untagged.
const COLOR_SPACES = {
  gbr: 'RGB (GBR)', bt709: 'BT.709', fcc: 'FCC', bt470bg: 'BT.601 (625 lines, PAL)',
  smpte170m: 'BT.601 (525 lines, NTSC)', smpte240m: 'SMPTE 240M', ycgco: 'YCgCo',
  bt2020nc: 'BT.2020 (non-constant luminance)', bt2020c: 'BT.2020 (constant luminance)', smpte2085: 'SMPTE ST 2085',
  'chroma-derived-nc': 'Chroma-derived (non-constant luminance)', 'chroma-derived-c': 'Chroma-derived (constant luminance)',
  ictcp: 'ICtCp',
};
// FFmpeg's codec names (ffprobe's codec_name) as a viewer knows them.
const CODECS = {
  h264: 'H.264 (AVC)', aac: 'AAC', mp3: 'MP3', opus: 'Opus', ac3: 'Dolby Digital (AC-3)', eac3: 'Dolby Digital Plus (E-AC-3)',
  flac: 'FLAC', alac: 'Apple Lossless (ALAC)', vorbis: 'Vorbis', dts: 'DTS', amr_nb: 'AMR-NB', amr_wb: 'AMR-WB',
};
// The pixel formats VideoInput gives (8-bit 4:2:0 only, so far: yuvj420p for MJPEG).
const PIXEL_FORMATS = { yuv420p: '4:2:0, 8 bits', yuvj420p: '4:2:0, 8 bits, full range' };

const section = card('About this file', {
  mark: 'core',
  markTitle: 'Open\'s description of the file, the YuvVideo handed to ShotDetector: each field checked against FFmpeg\'s, ' +
    'the audio track against ffprobe\'s',
});
section.id = 'about';
section.hidden = true;
const list = document.createElement('dl');
const raw = document.createElement('details');
raw.innerHTML = '<summary>JSON</summary><pre></pre>';
section.append(list, raw);

on('chose', () => {
  section.hidden = true;
  list.replaceChildren();
  raw.open = false;
  raw.querySelector('pre').textContent = '';
});

on('opened', ({ video: v }) => {
  const { num, den } = v.frameRate;
  const rate = num / den;
  list.replaceChildren();
  for (const [term, text, title] of [
    ['Video', CODECS[v.codec] ?? v.codec, `codec: ${v.codec}`],
    ['Size', `${v.width} × ${v.height}`, `width: ${v.width}, height: ${v.height}`],
    ['Frame rate', `${Number(rate.toFixed(3))} fps`, `frameRate: ${num}/${den}`],
    ['Pixel format', PIXEL_FORMATS[v.pixelFormat] ?? v.pixelFormat, `pixelFormat: ${v.pixelFormat}`],
    ['Colour space', v.colorSpace === '' ? 'Not tagged' : COLOR_SPACES[v.colorSpace] ?? v.colorSpace, `colorSpace: "${v.colorSpace}"`],
    ['Range', v.fullRange ? 'Full (0–255)' : 'Limited (16–235)', `fullRange: ${v.fullRange}`],
    ['Frames', v.frames.toLocaleString('en'), `frames: ${v.frames} (shown); frameCount: ${v.frameCount} (FFmpeg's nb_frames)`],
    ['Duration', v.duration == null ? 'Not known' : clock(v.duration), `duration: ${v.duration}`],
    ['Audio', v.audio == null ? 'No audio' : audio(v.audio), v.audio == null ? 'audio: null' :
      `codec: ${v.audio.codec}, sampleRate: ${v.audio.sampleRate}, channels: ${v.audio.channels}`],
  ]) {
    const dt = document.createElement('dt'), dd = document.createElement('dd');
    dt.textContent = term;
    dd.textContent = text;
    dd.title = title;
    list.append(dt, dd);
  }
  raw.querySelector('pre').textContent = JSON.stringify(v, null, 2);
  section.hidden = false;
});

// "AAC, 48 kHz, stereo".
function audio({ codec, sampleRate, channels }) {
  const layout = channels === 1 ? 'mono' : channels === 2 ? 'stereo' : `${channels} channels`;
  return `${CODECS[codec] ?? codec}, ${Number((sampleRate / 1000).toFixed(3))} kHz, ${layout}`;
}

// "4.56 s", "1:02.5" past a minute, "1:02:03.5" past an hour: the core's seconds to the millisecond, trailing zeros off.
function clock(seconds) {
  const s = Number(seconds.toFixed(3));
  if (s < 60) return `${s} s`;
  const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60;
  const rest = Number((s % 60).toFixed(3)), ss = `${rest < 10 ? '0' : ''}${rest}`;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}
