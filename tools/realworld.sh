#!/usr/bin/env bash
# The real-world check (README, "Checks"; docs/backlog.md, "Real-world check"): the core and the page's AOT bundle on real
# files, not only the generated samples, against ShotDetector's FFmpeg path, byte for byte, as tools/check.sh's native
# and wasm checks do. The files are Blender Foundation films (CC-BY) from download.blender.org, pinned by SHA-256, and
# variants of them made by the pinned FFmpeg for what no openly licensed real file we reach has (fragmented, rotated,
# interlaced): their bytes depend on that FFmpeg, as the demo clip's do (make-samples.sh), so they are pinned through
# their source and the FFmpeg that makes them. Kept in samples/realworld/ (gitignored, cached by realworld.yml).
#
# Usage: tools/realworld.sh list
#        tools/realworld.sh download [name...]      fetch, verify and make them; print the paths
#        tools/realworld.sh check [wasm] [name...]  the native check, then with wasm the AOT bundle under Node
#   (default: every file). Needs SHOTDETECTOR_FFMPEG_LIBS (an FFmpeg 8.1 shared build's folder, its ffmpeg and ffprobe
#   there too) as tools/check.sh native does; for wasm, Node and either BUNDLE (a published wwwroot, as realworld.yml's
#   bundle job uploads it) or the wasm-tools workload, to publish the App AOT here. Results in out/realworld/.
#
# Each file is expected either "same" (every check agrees) or "refused" (the core refuses it with an
# UnsupportedVideoException, today's scope: NativeCheck's and app-check.mjs's --reject). A row that widens the scope
# moves its file from refused to same here; the nightly run fails once a refused file opens ("Not refused").
# A file is "light" when it is over three minutes: the AOT bundle runs only the default and every-detector runs on it,
# whole and split, without stop, pause and resume (app-check.mjs --light; the owner's choice, 2026-10-31).
set -euo pipefail
cd "$(dirname "$0")/.."

dir=samples/realworld
blender=https://download.blender.org

# name|source|SHA-256 of the file kept (a zip's member's)|member of a zip, or the recipe of a made file|expect|light
# A made file's source is another row's name; its recipe is ffmpeg's arguments, @ standing for the input (-i source).
FILES=(
  # Sintel's trailer at 720p (Blender Foundation, CC-BY 3.0): 1280x720 High, 24 fps, 52 s, AAC 48 kHz; 7.6 MB.
  "sintel_trailer-720p.mp4|$blender/durian/trailer/sintel_trailer-720p.mp4|cb0fe73fc0a7d543459996c0cdab730997e6eac1013d3ede18796f777cb7f273||same|"
  # Big Buck Bunny, the whole film (Blender Foundation, CC-BY 3.0): 320x180 Constrained Baseline, 24 fps, 10 min, AAC
  # 48 kHz without an edit list (2008); a 65 MB zip.
  "BigBuckBunny_320x180.mp4|$blender/peach/bigbuckbunny_movies/BigBuckBunny_320x180.mp4.zip|f78f39603e6774907f2faafabf26a667f4a6fc31769ec304a8a8f7c62d280508|BigBuckBunny_320x180.mp4|same|light"
  # Caminandes: Gran Dillama (Blender Foundation, CC-BY 3.0): 1920x1080 High, 24 fps, 2.5 min, AAC 48 kHz; a 126 MB zip.
  "caminandes_gran_dillama.mp4|$blender/demo/movies/caminandes_gran_dillama.mp4.zip|468e6743c674689a728726bbe4bb4b2a65bd8702a89f021af26a8bb4d450eebd|caminandes_gran_dillama.mp4|same|"
  # Tears of Steel, the whole film (Blender Foundation, CC-BY 3.0), shotdetector's realworld.py's too: 1280x534 Main in
  # a QuickTime 'qt' file, 24 fps, 12 min, MP3 44.1 kHz (described, not measured); 372 MB.
  "tears_of_steel_720p.mov|$blender/demo/movies/ToS/tears_of_steel_720p.mov|efa9062d9cdb7a338e40ad530dfdf234806743f29ae6a1a136b97ece4e588e8f||same|light"
  # Elephants Dream, the whole film (Orange Open Movie Project, CC-BY 2.5): 480x270 Main, 11 min, AAC 48 kHz, a 'qt'
  # file whose packets are all 1/24 s but whose track is longer than they are: FFmpeg's average rate 367056/15691
  # (23.39), so refused as a variable rate today; 104 MB.
  "elephantsdream-480-h264-st-aac.mov|$blender/ED/elephantsdream-480-h264-st-aac.mov|a7dbacae5fa2bc345dbd7b10588575f3e2bba36012e9a93df5346467053b0634||refused|"
  # Made from those by the pinned FFmpeg. Caminandes as a fragmented MP4, the streams copied (a fragment a keyframe).
  "caminandes-fragmented.mp4|caminandes_gran_dillama.mp4||@ -c copy -movflags frag_keyframe+empty_moov+default_base_moof|same|"
  # Sintel's 720p trailer turned a quarter (a phone's portrait video: the track matrix, the streams copied); refused
  # until the rotation row (backlog, Waiting) lands.
  "sintel_trailer-720p-rotated.mp4|sintel_trailer-720p.mp4||-display_rotation:v:0 90 @ -c copy|refused|"
  # The first minute of Caminandes as broadcast-like 1080i25: top field first, MBAFF (x264's interlaced coding), 8 Mb/s,
  # AAC 192 kb/s.
  "caminandes-1080i25.mp4|caminandes_gran_dillama.mp4||@ -t 60 -vf fps=50,interlace=scan=tff,setfield=tff -c:v libx264 -preset fast -flags +ildct+ilme -x264-params tff=1 -b:v 8M -r 25 -c:a aac -b:a 192k|same|"
)

field() { local IFS='|'; local -a f; read -r -a f <<< "$1"; echo "${f[$2]:-}"; }
row() {
  for r in "${FILES[@]}"; do [[ $(field "$r" 0) == "$1" ]] && { echo "$r"; return; }; done
  echo "Unknown file: $1 (tools/realworld.sh list)" >&2; exit 2
}

libs=${SHOTDETECTOR_FFMPEG_LIBS:-}
ffmpeg_bin() { [[ -n $libs ]] && echo "$libs/$1" || echo "$1"; }

fetch() {
  local r name src sha member path
  r=$(row "$1"); name=$(field "$r" 0); src=$(field "$r" 1); sha=$(field "$r" 2); member=$(field "$r" 3)
  path=$dir/$name
  mkdir -p "$dir"
  if [[ $src != https://* ]]; then
    # A made file: from its source, by the pinned FFmpeg; made again when missing.
    if [[ ! -f $path ]]; then
      local input arg
      local -a args=()
      input=$(fetch "$src")
      # shellcheck disable=SC2086 # the recipe is ffmpeg's arguments, split on spaces.
      for arg in $member; do if [[ $arg == @ ]]; then args+=(-i "$input"); else args+=("$arg"); fi; done
      LD_LIBRARY_PATH=$libs${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH} "$(ffmpeg_bin ffmpeg)" -nostdin -v error -y \
        "${args[@]}" "$path.part.mp4" >&2
      mv "$path.part.mp4" "$path"
      echo "Made $name from $src: $(stat -c %s "$path") bytes, SHA-256 $(sha256sum < "$path" | cut -d' ' -f1)" >&2
    fi
    echo "$path"
    return
  fi
  if [[ ! -f $path ]]; then
    local download=$dir/.download-$name
    curl -fsSL --retry 5 --retry-all-errors --retry-delay 10 --connect-timeout 30 -o "$download" "$src" >&2 \
      || { rm -f "$download"; echo "::error::$src didn't download" >&2; exit 1; }
    if [[ -n $member ]]; then
      local unpacked
      unpacked=$(mktemp -d "$dir/.unzip-XXXXXX")
      unzip -q -o "$download" "$member" -d "$unpacked"
      mv "$unpacked/$member" "$path"
      rm -rf "$unpacked" "$download"
    else
      mv "$download" "$path"
    fi
    echo "Downloaded $name: $(stat -c %s "$path") bytes" >&2
  fi
  # Checked on every run, a cached file too (realworld.yml restores samples/realworld/).
  local got
  got=$(sha256sum < "$path" | cut -d' ' -f1)
  if [[ $got != "$sha" ]]; then
    rm -f "$path"
    echo "::error::$name from $src has SHA-256 $got, not the pinned $sha (deleted; the source changed?)" >&2; exit 1
  fi
  echo "$path"
}

names=()
command=${1:-}
[[ $# -gt 0 ]] && shift
wasm=false
for arg in "$@"; do
  case $arg in
    wasm) wasm=true ;;
    *) row "$arg" > /dev/null; names+=("$arg") ;;
  esac
done
if [[ ${#names[@]} == 0 ]]; then for r in "${FILES[@]}"; do names+=("$(field "$r" 0)"); done; fi

case $command in
  list)
    for r in "${FILES[@]}"; do echo "$(field "$r" 0) ($(field "$r" 4)$([[ -n $(field "$r" 5) ]] && echo ", light"))"; done
    exit 0 ;;
  download)
    for n in "${names[@]}"; do fetch "$n"; done
    exit 0 ;;
  check) ;;
  *) echo "Usage: tools/realworld.sh list | download [name...] | check [wasm] [name...]" >&2; exit 2 ;;
esac

: "${SHOTDETECTOR_FFMPEG_LIBS:?Set SHOTDETECTOR_FFMPEG_LIBS to the folder of an FFmpeg 8.1 shared build}"
export LD_LIBRARY_PATH=$SHOTDETECTOR_FFMPEG_LIBS${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
same=() refused=() full=() light=()
for n in "${names[@]}"; do
  path=$(fetch "$n")
  r=$(row "$n")
  if [[ $(field "$r" 4) == refused ]]; then refused+=("$path")
  else
    same+=("$path")
    if [[ -n $(field "$r" 5) ]]; then light+=("$path"); else full+=("$path"); fi
  fi
done

out=out/realworld
rm -rf "$out"
mkdir -p "$out"
summary() { echo "$1"; if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then echo "$1" >> "$GITHUB_STEP_SUMMARY"; fi; }

dotnet build tools/NativeCheck -c Release -v q -nologo
status=0
dotnet tools/NativeCheck/bin/Release/net10.0/NativeCheck.dll --out "$out/native" "${same[@]}" --reject "${refused[@]}" \
  | tee "$out/native-report.md" || status=$?
if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then { echo "## Real world, native: ManagedCodecs + FastYuv vs FFmpeg"; cat "$out/native-report.md"; } >> "$GITHUB_STEP_SUMMARY"; fi
if [[ $status != 0 ]]; then summary "Real world, native (${names[*]}): FAILED (above)."; exit 1; fi
[[ $wasm == true ]] || exit 0

# The page's bundle, AOT, as pages.yml deploys it (tools/check.sh's publish_app, without the threaded bundle, which
# app-check.mjs doesn't load).
if [[ -z ${BUNDLE:-} ]]; then
  rm -rf src/ShotDetectorWeb.App/obj src/ShotDetectorWeb.App/bin
  dotnet publish src/ShotDetectorWeb.App -c Release -v q -nologo -warnaserror -p:RunAOTCompilation=true
  BUNDLE=src/ShotDetectorWeb.App/bin/Release/net10.0/publish/wwwroot
fi
# The runtime's own notes (under Node, that it can't load its symbol file) come on stdout, starting MONO_WASM.
appcheck() {
  node tools/app-check.mjs "$BUNDLE" "$out/wasm" "$@" --native "$out/native" 2>> "$out/wasm-stderr.txt" \
    | { grep -v '^MONO_WASM:' || true; }
}
if [[ ${#full[@]} -gt 0 || ${#refused[@]} -gt 0 ]]; then appcheck "${full[@]}" --reject "${refused[@]}" || status=$?; fi
if [[ ${#light[@]} -gt 0 ]]; then appcheck "${light[@]}" --light || status=$?; fi
cat "$out/wasm-stderr.txt"
if [[ $status != 0 ]]; then summary "Real world, AOT under Node (${names[*]}): FAILED (above)."; exit 1; fi

# Every file the bundle wrote is the native run's, byte for byte; every native file of the runs it made has its twin.
files=0 differ=0 missing=0
for w in "$out"/wasm/*.csv "$out"/wasm/*.shots.json "$out"/wasm/*.shots.html; do
  [[ -f $w ]] || continue
  files=$((files + 1))
  n=$out/native/${w#"$out"/wasm/}
  if ! cmp -s "$n" "$w"; then echo "Differs: $w"; differ=$((differ + 1)); fi
done
for p in "${full[@]}" "${light[@]}"; do
  b=$(basename "${p%.*}")
  for n in "$out"/native/"$b".*.csv "$out"/native/"$b".*.shots.json "$out"/native/"$b".*.shots.html; do
    [[ -f $n && $n != *.ffmpeg.* ]] || continue
    run=${n#"$out"/native/"$b".}; run=${run%%.*}
    # A light file's runs are the default and every detector's (app-check.mjs's SPLIT_RUNS).
    if [[ " ${light[*]} " == *" $p "* && $run != adaptive && $run != all ]]; then continue; fi
    if [[ ! -f $out/wasm/${n#"$out"/native/} ]]; then echo "Not written by the bundle: ${n#"$out"/native/}"; missing=$((missing + 1)); fi
  done
done
summary "Real world, AOT under Node and native: $((files - differ)) of $files CSVs, JSON and HTML identical (${names[*]})."
if [[ $differ != 0 || $missing != 0 || $files == 0 ]] && [[ ${#same[@]} -gt 0 ]]; then exit 1; fi
