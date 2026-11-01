#!/usr/bin/env bash
# Generates the samples the checks run on (samples/, ignored by git): H.264 in MP4 from libx264 with hard cuts and fades,
# and MJPEG in AVI, each isolating something the YuvVideo description must get right, and a few the library must refuse.
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
python3 -I tools/mp4-edit-list.py "$out/edit-end-352x288-25.mp4" 0 3000:1024

# An edit list that starts at media_time 0 (Caminandes: Gran Dillama's, backlog "The first pts of a track whose edit
# list starts at 0"), over the whole media (4.56 s): libx264's 2-frame delay is then a first composition offset of 1024
# in 1/12800 that the edit doesn't skip. FFmpeg shifts the track so its first pts is 0 (mov_fix_index) and discards the
# last packets, past the edit; ffmpeg's muxer writes media_time = the first offset, so only a rewrite makes it.
enc -filter_complex "$(three 352x288 25 1.5)" -c:v libx264 -pix_fmt yuv420p "$out/edit-zero-352x288-25.mp4"
python3 -I tools/mp4-edit-list.py "$out/edit-zero-352x288-25.mp4" 0 4560:0
got=$("$fp" -v trace "$out/edit-zero-352x288-25.mp4" 2>&1 | grep -o 'edit list 0 - media time: [0-9]*' | head -1)
got+=,$("$fp" -v error -select_streams v -read_intervals %+#1 -show_entries stream=start_pts:packet=pts,dts -of csv=p=0 "$out/edit-zero-352x288-25.mp4" | tr '\n' ,)
[[ $got == 'edit list 0 - media time: 0,0,-1024,0,' ]] || { echo "edit-zero: its edit, first packet and start pts are $got" >&2; exit 1; }

# 720p, untagged: ShotDetector's size rule picks BT.709 there.
enc -filter_complex "$(three 1280x720 25 1),$untagged" -c:v libx264 -preset veryfast -pix_fmt yuv420p "$out/cuts-1280x720-25.mp4"

# With an AAC track that runs on 0.45 s past the video: the format's duration (DetectedVideo.Duration) spans both.
enc -filter_complex "$(three 320x240 25 1)" -f lavfi -i sine=d=3.45 -c:v libx264 -pix_fmt yuv420p -c:a aac "$out/with-audio-320x240-25.mp4"

# Fragmented (decision 2026-10-30-scope-widening.md), each a copy of one encode: an empty 'moov' and a fragment per
# keyframe, with AAC; no edit list, so the video starts at libx264's B-frame delay (start_pts 1024 in 1/12800) and FFmpeg
# knows no nb_frames. And the first fragment's samples in 'moov' (frag_keyframe alone): nb_frames counts those, 38 of 112.
f="$out/fragmented-320x240-25.mp4"
enc -filter_complex "$(three 320x240 25 1.5)" -f lavfi -i sine=d=4.8 -c:v libx264 -pix_fmt yuv420p -c:a aac "$out/fragmented.tmp.mp4"
enc -i "$out/fragmented.tmp.mp4" -c copy -movflags frag_keyframe+empty_moov "$f"
enc -i "$out/fragmented.tmp.mp4" -map 0:v -c copy -movflags frag_keyframe "$out/fragmented-moov-320x240-25.mp4"
rm "$out/fragmented.tmp.mp4"
# What they must be: fragments ('moof'), and the video's average rate, start and nb_frames as above.
got=
for s in "$f" "$out/fragmented-moov-320x240-25.mp4"; do
  (( $("$fp" -v trace "$s" 2>&1 | grep -c "type:'moof'") > 0 )) || { echo "$s: no 'moof' box." >&2; exit 1; }
  got+="$("$fp" -v error -select_streams v:0 -show_entries stream=avg_frame_rate,start_pts,nb_frames -of csv=p=0 "$s") "
done
[[ $got == '25/1,1024,N/A 25/1,1024,38 ' ]] || { echo "fragmented samples: avg_frame_rate, start_pts, nb_frames are $got" >&2; exit 1; }

# Interlaced (decision 2026-10-30-interlaced.md): three() at 50 fps woven into 25 fps frames of two fields each, coded as
# MBAFF frames by libx264 (-flags +ilme+ildct), top field first and bottom field first; and libx264's fake-interlaced,
# whose SPS allows field coding (frame_mbs_only_flag 0) while every picture is a progressive frame. Deinterlacing off
# (ShotDetector's default): the frames as decoded, woven.
weave="tinterlace=interleave_top"
enc -filter_complex "$(three 320x240 50 1.5),$weave,setfield=tff" -c:v libx264 -flags +ilme+ildct -pix_fmt yuv420p \
  "$out/interlaced-tff-320x240-25.mp4"
enc -filter_complex "$(three 320x240 50 1.5),$weave,setfield=bff" -c:v libx264 -flags +ilme+ildct -x264-params bff=1 \
  -pix_fmt yuv420p "$out/interlaced-bff-320x240-25.mp4"
enc -filter_complex "$(three 320x240 50 1.5),$weave" -c:v libx264 -x264-params fake-interlaced=1 -pix_fmt yuv420p \
  "$out/interlaced-fake-320x240-25.mp4"
# And the top-field-first one without its SEI (filter_units drops every SEI NAL unit, the picture timing's among them):
# the field order and each frame's top_field_first then come from the field order counts, interlaced_frame from the
# MBAFF coding (decision 2026-10-30-interlaced.md), what yadif's parity and --deinterlace auto go by.
enc -i "$out/interlaced-tff-320x240-25.mp4" -c copy -bsf:v filter_units=remove_types=6 "$out/interlaced-nosei-320x240-25.mp4"
# What they must be, from the bitstream (trace_headers: every SPS's frame_mbs_only_flag and mb_adaptive_frame_field_flag,
# every slice's field_pic_flag: frames, never field pictures) and from FFmpeg (the stream's field order, each frame's
# interlaced_frame,top_field_first), at 25 fps.
for f in tff:0:1:0:tt:1,1 bff:0:1:0:bb:1,0 fake:0:0:0:progressive:0,0 nosei:0:1:0:tt:1,1; do
  IFS=: read -r name mbs mbaff field order flags <<< "$f"
  s="$out/interlaced-$name-320x240-25.mp4"
  trace=$("$ff" -hide_banner -i "$s" -c copy -bsf:v trace_headers -f null - 2>&1)
  values() { awk -v n="$1" '$5 == n {print $NF}' <<< "$trace" | sort -u | tr '\n' ' '; }
  got="$(values frame_mbs_only_flag)$(values mb_adaptive_frame_field_flag)$(values field_pic_flag)"
  got+="$("$fp" -v error -select_streams v:0 -show_entries stream=field_order,avg_frame_rate -of csv=p=0 "$s") "
  got+=$("$fp" -v error -select_streams v:0 -show_entries frame=interlaced_frame,top_field_first -of csv=p=0 "$s" | cut -d, -f1,2 | sort -u | tr '\n' ' ')
  want="$mbs $mbaff $field $order,25/1 $flags "
  # The SEI kept or not: one in each picture of libx264's interlaced encodes, none in nosei's.
  sei=$(grep -c "Supplemental Enhancement Information" <<< "$trace" || true)
  if [[ $name == nosei ]]; then (( sei == 0 )) || want+="(no SEI, not $sei)"; else (( sei > 0 )) || want+="(SEI, not none)"; fi
  [[ $got == "$want" ]] || { echo "$s: frame_mbs_only, MBAFF, field_pic, rate, field order, frame flags are '$got', not '$want'." >&2; exit 1; }
done

# Rotated, as a phone records (decision 2026-10-30-scope-widening.md): three() coded turned the other way, and a display
# matrix (-display_rotation on a copy, counterclockwise) that shows it upright again: 90, 180 and 270, which FFmpeg reads
# as 90, -180 and -90 (av_display_rotation_get). The detectors and the picture's measures see the upright picture: black
# borders of four widths (left 8, right 24, top 20, bottom 40), so cropdetect's figures tell each side from the others.
for r in 90:transpose=clock 180:hflip,vflip 270:transpose=cclock; do
  enc -filter_complex "$(three 288x180 25 1),pad=320:240:8:20,${r#*:}" -c:v libx264 -pix_fmt yuv420p "$out/rotated.tmp.mp4"
  enc -display_rotation "${r%%:*}" -i "$out/rotated.tmp.mp4" -c copy "$out/rotated-${r%%:*}-320x240-25.mp4"
  rm "$out/rotated.tmp.mp4"
done
got=$(for r in 90 180 270; do
  "$fp" -v error -select_streams v:0 -show_entries stream=width,height:stream_side_data=rotation -of csv=p=0 \
    "$out/rotated-$r-320x240-25.mp4" | tr '\n' , ; echo -n ' '
done)
[[ $got == '240,320,90, 320,240,-180, 240,320,-90, ' ]] || { echo "rotated samples: coded size, rotation are '$got'" >&2; exit 1; }

# A variable frame rate, as a screen recording or a phone's (backlog "Variable frame rate"): three() of 2 s shots at 25 fps,
# every frame after frame 60 lasting twice as long (-fps_mode vfr keeps the timestamps), so a cut falls in each half;
# libx264's B-frames, an edit list. And a fragmented copy (an empty 'moov': the durations in each 'trun'). ShotDetector
# takes them at every frame's pts (YuvVideo.FrameTimestamps), FFmpeg's average rate 1250/79 (150 frames in 9.48 s).
vfr="setpts='if(lt(N,60),N,60+(N-60)*2)/25/TB'"
enc -filter_complex "$(three 320x240 25 2),$vfr" -fps_mode vfr -c:v libx264 -pix_fmt yuv420p "$out/vfr-320x240.mp4"
enc -i "$out/vfr-320x240.mp4" -c copy -movflags frag_keyframe+empty_moov "$out/vfr-fragmented-320x240.mp4"
# What they must be: packets of two durations (one frame and two, in 1/12800), the frames' pts 512 apart up to frame 60
# and 1024 after it, the average rate, and nb_frames (none with the empty 'moov').
got=
for s in vfr vfr-fragmented; do
  got+="$("$fp" -v error -select_streams v:0 -show_entries packet=duration -of csv=p=0 "$out/$s-320x240.mp4" | sort -un | tr '\n' ,)"
  got+="$("$fp" -v error -select_streams v:0 -show_entries frame=pts -of csv=p=0 "$out/$s-320x240.mp4" |
    awk 'NR > 1 {print (NR <= 61 ? "a" : "b") ($1 - p)} {p = $1}' | sort | uniq -c | awk '{printf "%s%s,", $2, $1}')"
  got+="$("$fp" -v error -select_streams v:0 -show_entries stream=avg_frame_rate,nb_frames -of csv=p=0 "$out/$s-320x240.mp4") "
done
want='512,1024,a51260,b102489,1250/79,150 512,1024,a51260,b102489,1250/79,N/A '
[[ $got == "$want" ]] || { echo "vfr samples: packet durations, frame steps, avg_frame_rate, nb_frames are '$got', not '$want'" >&2; exit 1; }

# MJPEG in AVI (decision 2026-10-30-mjpeg-avi.md): 4:2:0, which FFmpeg's decoder gives as yuvj420p (a JPEG is full range,
# tagged BT.601), every frame a keyframe. One without audio; one at 29.97 fps with PCM audio longer than the video, which
# doesn't count for FFmpeg's format duration (a PCM stream in AVI has none); one with MP3 longer than the video, which
# does (VBR audio: its header's length). -avoid_negative_ts disabled: else the muxer, shifting the MP3's priming, writes
# an empty video chunk after the first frame (a dropped frame, refused below).
enc -filter_complex "$(three 320x240 25 1.5),format=yuvj420p" -c:v mjpeg -q:v 3 "$out/mjpeg-320x240-25.avi"
enc -filter_complex "$(three 640x360 30000/1001 1.5),format=yuvj420p" -f lavfi -i sine=d=5 -c:v mjpeg -q:v 4 -c:a pcm_s16le \
  "$out/mjpeg-pcm-640x360-2997.avi"
enc -filter_complex "$(three 320x240 25 1),format=yuvj420p" -f lavfi -i sine=d=3.6 -c:v mjpeg -q:v 3 -c:a libmp3lame \
  -avoid_negative_ts disabled "$out/mjpeg-mp3-320x240-25.avi"
# What they must be: MJPEG at yuvj420p, every packet a keyframe and as many as nb_frames (no empty chunk), the rate, and
# the format's duration: the video's for the first two (114 frames at 25, 135 of 1001/30000 s), the MP3's for the last.
got=
for s in "$out/mjpeg-320x240-25.avi" "$out/mjpeg-pcm-640x360-2997.avi" "$out/mjpeg-mp3-320x240-25.avi"; do
  got+="$("$fp" -v error -select_streams v:0 -count_packets -show_entries stream=codec_name,pix_fmt,avg_frame_rate,nb_frames,nb_read_packets -of csv=p=0 "$s"),"
  got+="$("$fp" -v error -select_streams v:0 -show_entries packet=flags -of csv=p=0 "$s" | sort -u | tr -d '\n'),"
  got+="$("$fp" -v error -show_entries format=duration -of csv=p=0 "$s") "
done
want="mjpeg,yuvj420p,25/1,114,114,K__,4.560000 mjpeg,yuvj420p,30000/1001,135,135,K__,4.504500 mjpeg,yuvj420p,25/1,75,75,K__,3.631020 "
[[ $got == "$want" ]] || { echo "MJPEG samples: codec, pix_fmt, rate, nb_frames, packets, flags, duration are $got" >&2; exit 1; }
[[ $("$fp" -v error -select_streams a -show_entries stream=codec_name,duration -of csv=p=0 "$out/mjpeg-pcm-640x360-2997.avi") == pcm_s16le,N/A ]] \
  || { echo "mjpeg-pcm: the audio isn't PCM without a duration." >&2; exit 1; }

# 10-bit 4:2:0 and 8-bit 4:2:2 H.264 (libx264's High 10 and High 4:2:2; backlog "10-bit 4:2:0 and 8-bit 4:2:2"), which
# ShotDetector's frame source takes since 3.24.0, and 4:2:2 MJPEG (yuvj422p, most cameras'). 10-bit's conversion
# interpolates the chroma from where its samples sit (YuvVideo.ChromaLocation): one with no chroma location in its VUI
# (FFmpeg's H.264 default, left), one with chroma_sample_loc_type 2 (topleft), full range and BT.709. 4:2:2 limited and
# full range (yuvj422p).
enc -filter_complex "$(three 320x240 25 1.5)" -c:v libx264 -pix_fmt yuv420p10le "$out/yuv420p10-320x240-25.mp4"
# Its sources tagged BT.709 before the concat (which converts each input to the first's tags, testsrc2's BT.601), so
# that only the range is converted and the last shot is BT.709's colour bars at full range in 10 bits (PictureCheck).
bt709=setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709
enc -filter_complex "$(three 320x240 25 1.5 | sed "s/\[\([abc]\)\];/,$bt709[\1];/g")" \
  -c:v libx264 -pix_fmt yuv420p10le -chroma_sample_location topleft \
  -color_range pc -colorspace bt709 -color_primaries bt709 -color_trc bt709 "$out/yuv420p10-full-topleft-bt709-320x240-25.mp4"
enc -filter_complex "$(three 320x240 25 1.5)" -c:v libx264 -pix_fmt yuv422p "$out/yuv422p-320x240-25.mp4"
enc -filter_complex "$(three 320x240 25 1.5),format=yuvj422p" -c:v libx264 -pix_fmt yuvj422p -color_range pc "$out/yuvj422p-320x240-25.mp4"
enc -filter_complex "$(three 320x240 25 1.5),format=yuvj422p" -c:v mjpeg -q:v 3 "$out/mjpeg-422-320x240-25.avi"
# Odd heights, which ShotDetector's frame source takes for 8-bit 4:2:0 and 4:2:2 since 3.25.0 (backlog "Odd heights"):
# H.264 4:2:2 (its crop unit a row: libx264 refuses an odd 4:2:0 height) and 4:2:0 MJPEG, 320x239, the three shots cut
# to 239 rows in 4:4:4 first (testsrc2 rounds a 4:2:0 height down to even).
odd=format=yuv444p,crop=320:239:0:0
enc -filter_complex "$(three 320x240 25 1.5),$odd,format=yuv422p" -c:v libx264 -pix_fmt yuv422p "$out/odd-height-yuv422p-320x239-25.mp4"
enc -filter_complex "$(three 320x240 25 1.5),$odd,format=yuvj420p" -c:v mjpeg -q:v 3 "$out/mjpeg-odd-height-320x239-25.avi"
got=$(for f in odd-height-yuv422p-320x239-25.mp4 mjpeg-odd-height-320x239-25.avi; do
  "$fp" -v error -select_streams v:0 -show_entries stream=codec_name,pix_fmt,width,height -of csv=p=0 "$out/$f"; done | tr '\n' ' ')
got+=$("$ff" -hide_banner -i "$out/odd-height-yuv422p-320x239-25.mp4" -c copy -bsf:v trace_headers -f null - 2>&1 \
  | awk '$5 == "frame_crop_bottom_offset" {print $NF}' | sort -u)
[[ $got == 'h264,320,239,yuv422p mjpeg,320,239,yuvj420p 1' ]] || { echo "Odd heights: codec, size, pix_fmt and the SPS's bottom crop are $got" >&2; exit 1; }

# What they must be: from the bitstream (trace_headers: the SPS's chroma_format_idc, bit depths and chroma location
# flag and type), and from FFmpeg (profile, pix_fmt, range, matrix and the first frame's chroma location).
got=
for f in yuv420p10 yuv420p10-full-topleft-bt709 yuv422p yuvj422p; do
  s="$out/$f-320x240-25.mp4"
  trace=$("$ff" -hide_banner -i "$s" -c copy -bsf:v trace_headers -f null - 2>&1)
  for n in chroma_format_idc bit_depth_luma_minus8 bit_depth_chroma_minus8 chroma_loc_info_present_flag chroma_sample_loc_type_top_field; do
    got+="$(awk -v n="$n" '$5 == n {print $NF}' <<< "$trace" | sort -u | tr '\n' ' ')"
  done
  got+="$("$fp" -v error -select_streams v:0 -show_entries stream=profile,pix_fmt,color_range,color_space -of csv=p=0 "$s"),"
  got+="$("$fp" -v error -select_streams v:0 -read_intervals %+#1 -show_entries frame=chroma_location -of csv=p=0 "$s" | head -1 | tr -d ,)
"
done
got+="$("$fp" -v error -select_streams v:0 -show_entries stream=codec_name,pix_fmt -of csv=p=0 "$out/mjpeg-422-320x240-25.avi")"
want="1 2 2 0 High 10,yuv420p10le,tv,bt470bg,left
1 2 2 1 2 High 10,yuv420p10le,pc,bt709,topleft
2 0 0 0 High 4:2:2,yuv422p,tv,bt470bg,left
2 0 0 0 High 4:2:2,yuvj422p,pc,bt470bg,left
mjpeg,yuvj422p"
[[ $got == "$want" ]] || { printf '10-bit and 4:2:2 samples: chroma_format_idc, bit depths, chroma location, profile, pix_fmt, range, matrix, chroma location are\n%s\nnot\n%s\n' "$got" "$want" >&2; exit 1; }

# A stream whose colour tags change midway (backlog "Each frame's own colour tags"): two streams' Annex B concatenated,
# the first BT.709 at limited range, the second BT.601 (smpte170m) at full range, each with its own SPS, as a splice
# makes them. FFmpeg's decoder tags each frame with its SPS's, the stream with the first's ('avcC'), and ShotDetector
# converts each frame with its own (shotdetector#626).
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1.2 -c:v libx264 -bf 0 -pix_fmt yuv420p \
  -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv -f h264 "$out/tags-a.h264"
enc -f lavfi -i "mandelbrot=s=320x240:r=25,trim=duration=1.2,format=yuvj420p" -c:v libx264 -bf 0 -pix_fmt yuvj420p \
  -colorspace smpte170m -color_primaries smpte170m -color_trc smpte170m -color_range pc -f h264 "$out/tags-b.h264"
cat "$out/tags-a.h264" "$out/tags-b.h264" > "$out/tags.h264"
s="$out/tags-change-320x240-25.mp4"
enc -r 25 -i "$out/tags.h264" -c copy "$s"
rm "$out/tags-a.h264" "$out/tags-b.h264" "$out/tags.h264"
# What it must be: the SPSs in the bitstream (trace_headers: the first stream's in 'avcC' and in band, then the
# second's), BT.709 limited then BT.601 full; FFmpeg's frames tagged so, 30 each; the stream the first's.
got=$("$ff" -hide_banner -i "$s" -c copy -bsf:v trace_headers -f null - 2>&1 \
  | awk '$5 == "video_full_range_flag" || $5 == "matrix_coefficients" {print $5 "=" $NF}' | uniq | tr '\n' ' ')
got+=$("$fp" -v error -show_entries frame=color_range,color_space -of csv=p=0 "$s" | cut -d, -f1,2 | uniq -c | tr -s ' ' | tr '\n' ' ')
got+=$("$fp" -v error -show_entries stream=color_range,color_space -of csv=p=0 "$s")
want="video_full_range_flag=0 matrix_coefficients=1 video_full_range_flag=0 matrix_coefficients=1 video_full_range_flag=1 matrix_coefficients=6  30 tv,bt709  30 pc,smpte170m tv,bt709"
[[ $got == "$want" ]] || { printf 'tags-change: the SPSs, the frames and the stream (range, matrix) are\n%s\nnot\n%s\n' "$got" "$want" >&2; exit 1; }

# Bars and tone, for the audio measures: 6 s of a 1 kHz sine at -20 dBFS (sine's amplitude of 1/8 times 0.8: a peak of
# 0.1), 3 s of digital silence, 6 s of the tone again, in both channels of 48 kHz stereo AAC, under 15 s of video. The
# gap is 3 s, not 2, because FFmpeg's AAC encoder spreads the tone's edges into it by a few milliseconds: a 2 s gap
# came out 1.9966 s, too short for silencedetect's default 2 s (and so for the page, which shows the default's).
s="$out/tone-silence-320x240-25.mp4"
tone="sine=f=1000:r=48000:d=6,volume=0.8"
enc -filter_complex "testsrc2=s=320x240:r=25:d=15[v];$tone[a];anullsrc=r=48000:cl=mono,atrim=duration=3[b];$tone[c];[a][b][c]concat=n=3:v=0:a=1,pan=stereo|c0=c0|c1=c0[s]" \
  -map '[v]' -map '[s]' -c:v libx264 -pix_fmt yuv420p -c:a aac "$s"
# What it must be: one stereo 48 kHz AAC track of 15 s, the tone at -20 dBFS (an RMS level of -23.01 dB: the decoded
# peaks wander by a few tenths) and the gap silent below -60 dBFS for almost 3 s, found at silencedetect's default 2 s.
got=$("$fp" -v error -select_streams a -show_entries stream=codec_name,sample_rate,channels,duration -of default=nw=1 "$s")
want="codec_name=aac
sample_rate=48000
channels=2
duration=15.000000"
[[ $got == "$want" ]] || { printf '%s: the audio is\n%s\n' "$s" "$got" >&2; exit 1; }
level=$("$ff" -hide_banner -nostats -i "$s" -map 0:a -af atrim=start=1:end=5,astats=measure_perchannel=none:measure_overall=RMS_level -f null - 2>&1 | awk '/RMS level dB/ {print $NF}')
gap=$("$ff" -hide_banner -nostats -i "$s" -map 0:a -af silencedetect -f null - 2>&1 | awk '/silence_end/ {print $5, $8}')
if ! awk -v l="$level" -v g="$gap" 'BEGIN { split(g, x, " "); exit !(l > -23.06 && l < -22.96 && x[1] > 8.95 && x[1] < 9.05 && x[2] > 2.95 && x[2] < 3) }'; then
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

# Refused, each with its own message: 10-bit 4:2:2 and 8-bit 4:4:4 (ShotDetector's frame source takes neither), MPEG-4
# Part 2.
rm -f "$out/reject/yuv422p.mp4" "$out/reject/yuv420p10.mp4" "$out/reject/mjpeg-422.avi"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv422p10le "$out/reject/yuv422p10.mp4"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv444p "$out/reject/yuv444p.mp4"
got=$(for f in yuv422p10 yuv444p; do "$fp" -v error -show_entries stream=pix_fmt -of csv=p=0 "$out/reject/$f.mp4"; done | tr '\n' ' ')
[[ $got == 'yuv422p10le yuv444p ' ]] || { echo "H.264 rejects: pix_fmt are $got" >&2; exit 1; }
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v mpeg4 "$out/reject/mpeg4.mp4"
# A size that changes midway (320x240, then 352x288 from frame 25: two streams' Annex B concatenated, the second's SPS
# in band; no B-frames, so the raw stream's timestamps are plain), which Detect(path) scales to the first size and the
# frame source refuses (shotdetector#625): Mp4Input opens it, its frames refuse it.
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -bf 0 -pix_fmt yuv420p -f h264 "$out/reject/size-a.h264"
enc -f lavfi -i mandelbrot=s=352x288:r=25,trim=duration=1 -c:v libx264 -bf 0 -pix_fmt yuv420p -f h264 "$out/reject/size-b.h264"
cat "$out/reject/size-a.h264" "$out/reject/size-b.h264" > "$out/reject/size.h264"
enc -r 25 -i "$out/reject/size.h264" -c copy "$out/reject/size-change.mp4"
rm "$out/reject/size-a.h264" "$out/reject/size-b.h264" "$out/reject/size.h264"
got=$("$fp" -v error -show_entries frame=width,height -of csv=p=0 "$out/reject/size-change.mp4" | grep -v ',$' | uniq | tr '\n' ' ')
[[ $got == '320,240 352,288 ' ]] || { echo "size-change.mp4: its frames' sizes are $got" >&2; exit 1; }
# MJPEG at 4:4:4, which ShotDetector's frame source doesn't take yet; an AVI
# with an empty video chunk, a dropped frame (the muxer writes one after the first frame when it shifts MP3's priming),
# which FFmpeg counts and ManagedCodecs doesn't (ManagedCodecs#226); H.264 in AVI.
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1,format=yuvj444p -c:v mjpeg "$out/reject/mjpeg-444.avi"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=2,format=yuvj420p -f lavfi -i sine=d=3 -c:v mjpeg -c:a libmp3lame "$out/reject/mjpeg-dropped-frame.avi"
enc -f lavfi -i testsrc2=s=320x240:r=25:d=1 -c:v libx264 -pix_fmt yuv420p "$out/reject/h264.avi"
got=$(for f in mjpeg-444; do "$fp" -v error -show_entries stream=pix_fmt -of csv=p=0 "$out/reject/$f.avi"; done | tr '\n' ' ')
got+=$("$fp" -v error -select_streams v -count_packets -show_entries stream=nb_frames,nb_read_packets -of csv=p=0 "$out/reject/mjpeg-dropped-frame.avi")
[[ $got == 'yuvj444p 51,50' ]] || { echo "MJPEG rejects: pix_fmt and the dropped frame's nb_frames, packets are $got" >&2; exit 1; }
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
echo "Samples in $out/: $(ls "$out"/*.mp4 "$out"/*.avi | wc -l) to compare, $(ls "$out"/reject | wc -l) to refuse, $(ls "$out"/no-aac | wc -l) without AAC to measure."
