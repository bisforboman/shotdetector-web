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
// The pixel formats VideoInput gives (8-bit 4:2:0 and 4:2:2, 10-bit 4:2:0, so far: yuvj420p or yuvj422p for MJPEG; at
// 10 bits the range is fullRange's alone).
const PIXEL_FORMATS = {
  yuv420p: '4:2:0, 8 bits', yuvj420p: '4:2:0, 8 bits, full range', yuv422p: '4:2:2, 8 bits',
  yuvj422p: '4:2:2, 8 bits, full range', yuv420p10le: '4:2:0, 10 bits',
};

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
    ['Size', size(v), `width: ${v.width}, height: ${v.height} (as decoded)`],
    ['Rotation', rotation(v.rotation), `rotation: ${v.rotation} (degrees counterclockwise, ffprobe's)`],
    ['Frame rate', v.frameTimestamps ? `Variable, ${Number(rate.toFixed(3))} fps on average` : `${Number(rate.toFixed(3))} fps`,
      `frameRate: ${num}/${den} (FFmpeg's avg_frame_rate)${v.frameTimestamps ? '; frameTimestamps: every frame\'s pts, ' +
        'as the frames are detected at their own times' : ''}`],
    ['Scan', FIELD_ORDERS[v.fieldOrder]?.[0] ?? v.fieldOrder, `fieldOrder: ${v.fieldOrder} (ffprobe's field_order: ` +
      `${FIELD_ORDERS[v.fieldOrder]?.[1] ?? '?'}), what Deinterlace auto goes by`],
    ['Pixel format', PIXEL_FORMATS[v.pixelFormat] ?? v.pixelFormat, `pixelFormat: ${v.pixelFormat}`],
    ['Colour space', v.colorSpace === '' ? 'Not tagged' : COLOR_SPACES[v.colorSpace] ?? v.colorSpace, `colorSpace: "${v.colorSpace}"`],
    ['Range', range(v), `fullRange: ${v.fullRange}`],
    ['Chroma siting', v.chromaLocation == null ? 'Not given (centre)' : CHROMA_LOCATIONS[v.chromaLocation] ?? v.chromaLocation,
      `chromaLocation: ${v.chromaLocation == null ? 'null' : `"${v.chromaLocation}"`} (ffprobe's chroma_location; ` +
      'what a 10-bit video\'s conversion goes by)'],
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
  // Every frame's pts (a variable rate's) on one line, not one a line.
  raw.querySelector('pre').textContent = JSON.stringify(v, (key, value) => key === 'frameTimestamps' && value ?
    `[${value.length} pts: ${value.slice(0, 4).join(', ')}, …, ${value.at(-1)}]` : value, 2);
  section.hidden = false;
});

// FFmpeg's chroma locations (av_chroma_location_name) as a viewer knows them.
const CHROMA_LOCATIONS = {
  left: 'Left (co-sited horizontally)', center: 'Centre', topleft: 'Top left (co-sited)', top: 'Top',
  bottomleft: 'Bottom left', bottom: 'Bottom',
};

// The range's code values at the video's depth.
function range({ fullRange, pixelFormat }) {
  const tenBit = pixelFormat.endsWith('10le');
  if (fullRange) return tenBit ? 'Full (0–1023)' : 'Full (0–255)';
  return tenBit ? 'Limited (64–940)' : 'Limited (16–235)';
}

// The stream's field order (FFmpeg's, as ShotDetector's FrameReader names it): in a viewer's words, and ffprobe's.
const FIELD_ORDERS = {
  Unknown: ['Not known', 'unknown'],
  Progressive: ['Progressive', 'progressive'],
  TopFirst: ['Interlaced, top field first', 'tt'],
  BottomFirst: ['Interlaced, bottom field first', 'bb'],
  TopCodedBottomFirst: ['Interlaced, top field coded first, bottom shown first', 'tb'],
  BottomCodedTopFirst: ['Interlaced, bottom field coded first, top shown first', 'bt'],
};

// The picture's size as it is shown: a video turned a quarter (its display matrix) the other way up.
function size(v) {
  return Math.abs(v.rotation) % 180 === 90 ? `${v.height} × ${v.width} (turned; coded ${v.width} × ${v.height})`
    : `${v.width} × ${v.height}`;
}

// "Upright", "Shown a quarter turn clockwise", ...: the turn that shows the frames as decoded upright (ffprobe's rotation is
// counterclockwise).
function rotation(degrees) {
  const turn = ((-degrees % 360) + 360) % 360;
  return turn === 0 ? 'Upright' : turn === 180 ? 'Shown upside down' :
    `Shown a quarter turn ${turn === 90 ? 'clockwise' : 'counterclockwise'}`;
}

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
