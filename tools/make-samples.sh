#!/usr/bin/env bash
# Generates the sample MP4s the checks run on (samples/, ignored by git): H.264 from libx264 with hard cuts and fades,
# each isolating something the YuvVideo description must get right, and a few the library must refuse.
#
# Usage: tools/make-samples.sh
#   FFMPEG: the ffmpeg to use (default: ffmpeg on PATH); it needs libx264. Python 3 for the edit list rewrite.
#   FFPROBE: the ffprobe to verify with (default: the one beside FFMPEG).
#   SINTEL: Sintel's trailer as Blender publishes it (sintel_trailer-1080p.mp4, the one ci.yml downloads): re-encoded
#   to samples/sintel_trailer-480p.mp4 (854x480, 24 fps, about 10 MB), the real trailer the checks and the demo run.
set -euo pipefail
cd "$(dirname "$0")/.."
ff=${FFMPEG:-ffmpeg}
if [[ $ff == */* ]]; then fp=${FFPROBE:-$(dirname "$ff")/ffprobe}; else fp=${FFPROBE:-ffprobe}; fi
out=samples
mkdir -p "$out" "$out/reject"

enc() { "$ff" -hide_banner -loglevel error -y "$@"; }

# Three shots of different content, cut hard, at size $1 and rate $2, through extra filters $3.
three() {
  local size=$1 rate=$2 d=$3
  echo "testsrc2=s=$size:r=$rate:d=$d[a];mandelbrot=s=$size:r=$rate,trim=duration=$d[b];smptehdbars=s=$size:r=$rate:d=$d[c];[a][b][c]concat=n=3:v=1:a=0"
}

# No colour tags at all (no VUI signal type, no 'colr' values): ShotDetector picks the matrix by size.
untagged=setparams=colorspace=unknown:color_primaries=unknown:color_trc=unknown:range=unknown

# 25 fps, 854x480 (not a multiple of 16: the SPS crops), libx264's defaults (High, CABAC, B-frames with b-pyramid),
# untagged.
enc -filter_complex "$(three 854x480 25 2),$untagged" -c:v libx264 -pix_fmt yuv420p "$out/cuts-854x480-25.mp4"

# 29.97 fps (timescale 30000, delta 1001), BT.709 tagged in the SPS and in 'colr', three B-frames.
enc -filter_complex "$(three 640x360 30000/1001 2)" -c:v libx264 -bf 3 -pix_fmt yuv420p \
  -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv "$out/cuts-640x360-2997-bt709.mp4"

# 23.976 fps, Baseline (CAVLC, no B-frames), BT.601 (smpte170m) tags.
enc -filter_complex "$(three 480x270 24000/1001 2)" -c:v libx264 -profile:v baseline -pix_fmt yuv420p \
  -colorspace smpte170m -color_primaries smpte170m -color_trc smpte170m "$out/baseline-480x270-23976-bt601.mp4"

# Fades to and from black between shots, for detect-threshold. Tagged BT.601 (bt470bg), as ffmpeg tags its test sources.
enc -filter_complex "testsrc2=s=320x240:r=25:d=2,fade=t=out:st=1.4:d=0.6[a];color=black:s=320x240:r=25:d=0.4[k];mandelbrot=s=320x240:r=25,trim=duration=2,fade=t=in:st=0:d=0.6,fade=t=out:st=1.5:d=0.5[b];smptebars=s=320x240:r=25:d=2,fade=t=in:st=0:d=0.3[c];[a][k][b][c]concat=n=4:v=1:a=0" \
  -c:v libx264 -pix_fmt yuv420p "$out/fades-320x240-25.mp4"

# Full range (yuvj420p: video_full_range_flag in the SPS, and 'colr' nclx full range).
enc -filter_complex "$(three 320x240 25 1.5),format=yuvj420p" -c:v libx264 -pix_fmt yuvj420p -color_range pc "$out/full-range-320x240-25.mp4"

# A stream that starts at 1.4 s: the muxer writes an empty edit before the media (start pts 17920 in 1/12800).
enc -filter_complex "$(three 352x288 25 1.5)" -c:v libx264 -pix_fmt yuv420p -output_ts_offset 1.4 "$out/start-offset-352x288-25.mp4"

# An edit list that ends 3 s in (of 4.5 s): FFmpeg plays 75 frames, while nb_frames (and so OpenCV's frame count) stays
# the track's 112 samples. Movie timescale 1000; libx264's 2-frame delay is media_time 1024 in 1/12800.
enc -filter_complex "$(three 352x288 25 1.5)" -c:v libx264 -pix_fmt yuv420p "$out/edit-end-352x288-25.mp4"
python3 -I external/ManagedCodecs/tools/mp4-edit-list.py "$out/edit-end-352x288-25.mp4" 0 3000:1024

# 720p, untagged: ShotDetector's size rule picks BT.709 there.
enc -filter_complex "$(three 1280x720 25 1),$untagged" -c:v libx264 -preset veryfast -pix_fmt yuv420p "$out/cuts-1280x720-25.mp4"

# With an AAC track that runs on 0.45 s past the video: the format's duration (DetectedVideo.Duration) spans both.
enc -filter_complex "$(three 320x240 25 1)" -f lavfi -i sine=d=3.45 -c:v libx264 -pix_fmt yuv420p -c:a aac "$out/with-audio-320x240-25.mp4"

# Bars and tone, for the audio measures: 6 s of a 1 kHz sine at -20 dBFS (sine's amplitude of 1/8 times 0.8: a peak of
# 0.1), 2 s of digital silence, 6 s of the tone again, in both channels of 48 kHz stereo AAC, under 14 s of video.
s="$out/tone-silence-320x240-25.mp4"
tone="sine=f=1000:r=48000:d=6,volume=0.8"
enc -filter_complex "testsrc2=s=320x240:r=25:d=14[v];$tone[a];anullsrc=r=48000:cl=mono,atrim=duration=2[b];$tone[c];[a][b][c]concat=n=3:v=0:a=1,pan=stereo|c0=c0|c1=c0[s]" \
  -map '[v]' -map '[s]' -c:v libx264 -pix_fmt yuv420p -c:a aac "$s"
# What it must be: one stereo 48 kHz AAC track of 14 s, the tone at -20 dBFS (an RMS level of -23.01 dB: the decoded
# peaks wander by a few tenths) and the gap silent below -60 dBFS for almost 2 s (the encoder spreads the tone's edges
# into it by a few milliseconds).
got=$("$fp" -v error -select_streams a -show_entries stream=codec_name,sample_rate,channels,duration -of default=nw=1 "$s")
want="codec_name=aac
sample_rate=48000
channels=2
duration=14.000000"
[[ $got == "$want" ]] || { printf '%s: the audio is\n%s\n' "$s" "$got" >&2; exit 1; }
level=$("$ff" -hide_banner -nostats -i "$s" -map 0:a -af atrim=start=1:end=5,astats=measure_perchannel=none:measure_overall=RMS_level -f null - 2>&1 | awk '/RMS level dB/ {print $NF}')
gap=$("$ff" -hide_banner -nostats -i "$s" -map 0:a -af silencedetect=d=1.9 -f null - 2>&1 | awk '/silence_end/ {print $5, $8}')
if ! awk -v l="$level" -v g="$gap" 'BEGIN { split(g, x, " "); exit !(l > -23.06 && l < -22.96 && x[1] > 7.95 && x[1] < 8.05 && x[2] > 1.95 && x[2] < 2) }'; then
  echo "$s: the tone's RMS level is ${level:-?} dB, the gap (end, length) is ${gap:-not found}." >&2; exit 1
fi

# Sintel's trailer (Blender Foundation, CC-BY 3.0; 1920x1080, 24 fps, 52 s, untagged), re-encoded to what the library
# takes: 8-bit 4:2:0 progressive H.264 (libx264's High) at a constant 24 fps in a non-fragmented MP4, 854x480, two-pass
# at 1400 kb/s with AAC at 128 kb/s, about 10 MB. Tagged BT.709, as the HD original is (untagged at 480p it would read
# as BT.601). The demo on the page is this same file.
if [[ -n ${SINTEL:-} ]]; then
  [[ -f $SINTEL ]] || { echo "SINTEL=$SINTEL: no such file." >&2; exit 1; }
  s="$out/sintel_trailer-480p.mp4"
  log=$(mktemp -d)
  x264=(-map 0:v:0 -vf scale=854:480,setsar=1,fps=24,setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv
    -c:v libx264 -preset slow -b:v 1400k -pix_fmt yuv420p -passlogfile "$log/x264")
  enc -i "$SINTEL" "${x264[@]}" -pass 1 -an -f mp4 /dev/null
  enc -i "$SINTEL" "${x264[@]}" -pass 2 -map 0:a:0 -c:a aac -b:a 128k -map_metadata -1 "$s"
  rm -rf "$log"

  # What it must be, from ffprobe: one H.264 and one AAC stream, the video as above, every frame lasting one 24th of a
  # second, no 'moof' box, each SPS frame_mbs_only (progressive), and about 10 MB.
  probe() { "$fp" -v error -select_streams "$1" -show_entries "$2" -of default=nw=1 "$s"; }
  want="codec_name=h264
profile=High
width=854
height=480
pix_fmt=yuv420p
color_range=tv
color_space=bt709
color_transfer=bt709
color_primaries=bt709
field_order=progressive
r_frame_rate=24/1
avg_frame_rate=24/1"
  got=$(probe v:0 stream=codec_name,profile,width,height,pix_fmt,field_order,color_range,color_space,color_transfer,color_primaries,r_frame_rate,avg_frame_rate)
  bad=()
  [[ $got == "$want" ]] || bad+=("the video stream is
$got")
  [[ $(probe a stream=codec_name) == codec_name=aac ]] || bad+=("the audio isn't one AAC stream")
  [[ $("$fp" -v error -show_entries stream=index -of csv=p=0 "$s" | wc -l) == 2 ]] || bad+=("not two streams")
  durations=$(probe v:0 packet=duration_time | sort | uniq -c | tr -s ' \n' ' ')
  [[ $durations == ' '*' duration_time=0.041667 ' ]] || bad+=("frame durations:$durations")
  if "$fp" -v trace "$s" 2>&1 | grep -q "type:'moof'"; then bad+=("fragmented ('moof')"); fi
  mbs=$("$ff" -hide_banner -i "$s" -map 0:v:0 -c copy -bsf:v trace_headers -f null - 2>&1 | awk '/frame_mbs_only_flag/ {print $NF}' | sort -u)
  [[ $mbs == 1 ]] || bad+=("frame_mbs_only_flag: ${mbs:-none found}")
  size=$(stat -c %s "$s")
  (( size > 8000000 && size < 12000000 )) || bad+=("$size bytes")
  if (( ${#bad[@]} )); then printf '%s is not what the recipe makes:\n' "$s" >&2; printf '  %s\n' "${bad[@]}" >&2; exit 1; fi
  echo "$s: $(probe v:0 stream=nb_frames | cut -d= -f2) frames at 24 fps, 854x480, $size bytes."
fi

# Refused, each with its own message: 4:2:2, 10 bits, MPEG-4 Part 2, a variable frame rate, an AVI, rotation.
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv422p "$out/reject/yuv422p.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv420p10le "$out/reject/yuv420p10.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v mpeg4 "$out/reject/mpeg4.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=2 -vf "setpts='if(lt(N,25),N,25+(N-25)*2)/25/TB'" -fps_mode vfr -c:v libx264 -pix_fmt yuv420p "$out/reject/vfr.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v mjpeg "$out/reject/mjpeg.avi"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv420p "$out/reject/unrotated.tmp.mp4"
enc -display_rotation 90 -i "$out/reject/unrotated.tmp.mp4" -c copy "$out/reject/rotated.mp4"
rm "$out/reject/unrotated.tmp.mp4"
# Audio the measures leave out, each with a reason and not an error (NativeCheck's audio check): MP3 and Opus in MP4,
# and AAC Main, which ManagedCodecs' decoder refuses (an AAC LC file with its AudioSpecificConfig's object type made 1:
# FFmpeg's encoder writes LC only).
mkdir -p "$out/no-aac"
short=(-f lavfi -i testsrc2=s=160x120:r=25:d=1 -f lavfi -i sine=d=1 -c:v libx264 -pix_fmt yuv420p)
enc "${short[@]}" -c:a libmp3lame "$out/no-aac/mp3.mp4"
enc "${short[@]}" -c:a libopus "$out/no-aac/opus.mp4"
enc "${short[@]}" -c:a aac "$out/no-aac/aac-main.mp4"
python3 -I - "$out/no-aac/aac-main.mp4" <<'PY'
import sys
path = sys.argv[1]
d = bytearray(open(path, "rb").read())
def descriptor(i):
    """An MPEG-4 descriptor at i: its tag, where its payload starts, its length."""
    tag, i, n = d[i], i + 1, 0
    while True:
        b, i = d[i], i + 1
        n = n << 7 | b & 0x7F
        if not b & 0x80:
            return tag, i, n
# esds: version and flags, then ES_Descriptor (ES_ID, flags: none set by FFmpeg), DecoderConfigDescriptor (13 bytes),
# DecSpecificInfo: the AudioSpecificConfig, whose first 5 bits are the object type.
tag, i, _ = descriptor(d.index(b"esds") + 8)
assert tag == 3 and d[i + 2] == 0, "unexpected ES_Descriptor"
tag, i, _ = descriptor(i + 3)
assert tag == 4, "no DecoderConfigDescriptor"
tag, i, _ = descriptor(i + 13)
assert tag == 5 and d[i] >> 3 == 2, "no AAC LC AudioSpecificConfig"
d[i] = 1 << 3 | d[i] & 7
open(path, "wb").write(d)
PY
for f in mp3:mp3 opus:opus aac-main:aac/Main; do
  got=$("$fp" -v error -select_streams a -show_entries stream=codec_name,profile -of csv=p=0 "$out/no-aac/${f%%:*}.mp4" | tr , /)
  [[ $got == "${f#*:}"* ]] || { echo "$out/no-aac/${f%%:*}.mp4: the audio is $got, not ${f#*:}." >&2; exit 1; }
done
echo "Samples in $out/: $(ls "$out"/*.mp4 | wc -l) to compare, $(ls "$out"/reject | wc -l) to refuse, $(ls "$out"/no-aac | wc -l) without AAC to measure."
