#!/usr/bin/env bash
# Generates the sample MP4s the checks run on (samples/, ignored by git): H.264 from libx264 with hard cuts and fades,
# each isolating something the YuvVideo description must get right, and a few the library must refuse.
#
# Usage: tools/make-samples.sh
#   FFMPEG: the ffmpeg to use (default: ffmpeg on PATH); it needs libx264. Python 3 for the edit list rewrite.
#   Also copies a real trailer (Sintel, 640x360) when SINTEL points at one.
set -euo pipefail
cd "$(dirname "$0")/.."
ff=${FFMPEG:-ffmpeg}
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

if [[ -n ${SINTEL:-} && -f $SINTEL ]]; then cp "$SINTEL" "$out/sintel_trailer-480p.mp4"; fi

# Refused, each with its own message: 4:2:2, 10 bits, MPEG-4 Part 2, a variable frame rate, an AVI, rotation.
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv422p "$out/reject/yuv422p.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv420p10le "$out/reject/yuv420p10.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v mpeg4 "$out/reject/mpeg4.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=2 -vf "setpts='if(lt(N,25),N,25+(N-25)*2)/25/TB'" -fps_mode vfr -c:v libx264 -pix_fmt yuv420p "$out/reject/vfr.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v mjpeg "$out/reject/mjpeg.avi"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv420p "$out/reject/unrotated.tmp.mp4"
enc -display_rotation 90 -i "$out/reject/unrotated.tmp.mp4" -c copy "$out/reject/rotated.mp4"
rm "$out/reject/unrotated.tmp.mp4"
echo "Samples in $out/: $(ls "$out"/*.mp4 | wc -l) to compare, $(ls "$out"/reject | wc -l) to refuse."
