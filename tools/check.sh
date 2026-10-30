#!/usr/bin/env bash
# The exactness checks (README, "Checks"):
#   native: ShotDetectorWeb.Core (ManagedCodecs + FastYuv) against ShotDetector's Detect(path) through FFmpeg in-process,
#           every detector, scene and stats CSVs byte for byte (tools/NativeCheck);
#   wasm:   the same library on browser-wasm under Node, through the page's own exports (src/ShotDetectorWeb.App,
#           driven by tools/app-check.mjs), its CSVs against the native run's;
#   page:   the page itself in Chromium (tools/page-check, Playwright): the published wwwroot served with the demo clip,
#           PAGE_SAMPLE picked on one worker and on two, the demo paused and resumed, then stopped while paused, each
#           run's downloaded CSVs against the native run's (the samples named are not used: always those two). Reuses the bundle wasm published in the same
#           call, else publishes one.
#   replace: LGPL-2.1, FastYuv replaceable: one built from its source at the package's tag, with a visible change, in a
#           bundle rebuilt with -p:FastYuvAssembly, must be the one that runs (tools/replace-check.mjs).
#
# Usage: tools/check.sh [native] [wasm] [page] [replace] [aot] [<sample.mp4>...]
#   (default: native wasm; aot: the wasm and replace bundles compiled ahead of time; samples named: only those, instead
#   of every samples/*.mp4, the files to refuse and those without AAC still all: CI's interpreted check on a pull
#   request, README "Checks". wasm compares the CSVs of the samples it ran, so native may have run more.)
#   Needs samples/ (tools/make-samples.sh), SHOTDETECTOR_FFMPEG_LIBS (an FFmpeg 8.1 shared build's folder, also on
#   LD_LIBRARY_PATH) for native, Node plus the wasm-tools workload (dotnet workload install wasm-tools) for wasm, page
#   and replace, Playwright and its Chromium for page (npm ci in tools/page-check, then npx playwright install
#   chromium there; or one require finds), and git with access to github.com for replace.
set -euo pipefail
cd "$(dirname "$0")/.."

native=false wasm=false page=false replace=false aot=false
samples=()
for arg in "$@"; do
  case $arg in
    native) native=true ;;
    wasm) wasm=true ;;
    page) page=true ;;
    replace) replace=true ;;
    aot) aot=true ;;
    *.mp4) [[ -f $arg ]] || { echo "No such sample: $arg" >&2; exit 2; }; samples+=("$arg") ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done
if [[ $native == false && $wasm == false && $page == false && $replace == false ]]; then native=true wasm=true; fi

shopt -s nullglob
if [[ ${#samples[@]} == 0 ]]; then samples=(samples/*.mp4); fi
rejects=(samples/reject/*)
noAac=(samples/no-aac/*)
if [[ ${#samples[@]} == 0 ]]; then echo "No samples: run tools/make-samples.sh first." >&2; exit 1; fi

if [[ $native == true ]]; then
  : "${SHOTDETECTOR_FFMPEG_LIBS:?Set SHOTDETECTOR_FFMPEG_LIBS to the folder of an FFmpeg 8.1 shared build}"
  export LD_LIBRARY_PATH=$SHOTDETECTOR_FFMPEG_LIBS${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
  dotnet build tools/NativeCheck -c Release -v q -nologo
  rm -rf out/native
  mkdir -p out
  status=0
  dotnet tools/NativeCheck/bin/Release/net10.0/NativeCheck.dll --out out/native "${samples[@]}" --reject "${rejects[@]}" | tee out/native-report.md || status=$?
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then { echo "## Native: ManagedCodecs + FastYuv vs FFmpeg"; cat out/native-report.md; } >> "$GITHUB_STEP_SUMMARY"; fi
  if [[ $status != 0 ]]; then echo "The native check failed (above)." >&2; exit 1; fi
fi

if [[ $wasm == true ]]; then
  if [[ ! -d out/native ]]; then echo "No native results to compare with: run tools/check.sh native first." >&2; exit 1; fi
  publish=src/ShotDetectorWeb.App/bin/Release/net10.0/publish
  rm -rf "$publish"
  dotnet publish src/ShotDetectorWeb.App -c Release -v q -nologo -warnaserror -p:RunAOTCompilation=$aot
  # LGPL-2.1: FastYuv is its own file in the bundle, its license in the publish output.
  for f in wwwroot/_framework/ShotDetector.FastYuv.wasm licenses/ShotDetector.FastYuv/COPYING.LGPL; do
    if [[ ! -f $publish/$f ]]; then echo "Not in the App's publish output: $f" >&2; exit 1; fi
  done
  rm -rf out/wasm
  # The runtime's own notes (under Node, that it can't load its symbol file) come on stdout, starting MONO_WASM.
  status=0
  node tools/app-check.mjs "$publish/wwwroot" out/wasm "${samples[@]}" --reject "${rejects[@]}" --no-aac "${noAac[@]}" \
    --native out/native 2> out/wasm-stderr.txt \
    | { grep -v '^MONO_WASM:' || true; } || status=$?
  cat out/wasm-stderr.txt
  if [[ $status != 0 ]]; then echo "The WebAssembly run failed (above)." >&2; exit 1; fi
  # Interpreted, FFmpeg.AutoGen must not be loaded at all; AOT loads some assemblies before the first call, FFmpeg.AutoGen
  # among them, so there only the runs must not load it.
  if [[ $aot == true ]]; then expect='^No FFmpeg assembly loaded'; else expect='^No FFmpeg assembly loaded at all\.$'; fi
  if ! grep -qE "$expect" out/wasm-stderr.txt; then echo "The WebAssembly run failed or loaded FFmpeg (above)." >&2; exit 1; fi

  # The native CSVs of the samples run here (all of out/native/ unless samples were named), each written by the run.
  files=0 differ=0 extra=0 missing=0
  for s in "${samples[@]}"; do
    csvs=(out/native/"$(basename "$s" .mp4)".*.csv)
    if [[ ${#csvs[@]} == 0 ]]; then echo "No native CSVs for $s: run tools/check.sh native on it first."; missing=$((missing + 1)); fi
    for n in "${csvs[@]}"; do
      [[ $n == *.ffmpeg.* ]] && continue
      files=$((files + 1))
      w=out/wasm/${n#out/native/}
      if ! cmp -s "$n" "$w"; then echo "Differs: $w"; differ=$((differ + 1)); fi
    done
  done
  for w in out/wasm/*.csv; do
    if [[ ! -f out/native/${w#out/wasm/} ]]; then echo "Not written natively: $w"; extra=$((extra + 1)); fi
  done
  summary="WebAssembly (AOT: $aot) and native: $((files - differ)) of $files CSVs identical (${#samples[@]} samples)."
  echo "$summary"
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then echo "$summary" >> "$GITHUB_STEP_SUMMARY"; fi
  if [[ $differ != 0 || $extra != 0 || $missing != 0 ]]; then exit 1; fi
fi

if [[ $page == true ]]; then
  # A short sample with audio and keyframes enough for two ranges, and the demo clip as pages.yml makes it.
  sample=${PAGE_SAMPLE:-samples/with-audio-320x240-25.mp4}
  demo=samples/sintel_trailer-480p.mp4
  for f in "$sample" "$demo"; do [[ -f $f ]] || { echo "No such sample: $f (tools/make-samples.sh)" >&2; exit 1; }; done
  if [[ ! -d out/native ]]; then echo "No native results to compare with: run tools/check.sh native first." >&2; exit 1; fi
  publish=src/ShotDetectorWeb.App/bin/Release/net10.0/publish
  if [[ $wasm == false ]]; then
    rm -rf "$publish"
    dotnet publish src/ShotDetectorWeb.App -c Release -v q -nologo -warnaserror -p:RunAOTCompilation=$aot
  fi
  rm -rf out/page
  status=0
  node tools/page-check/page-check.mjs "$publish/wwwroot" out/page "$sample" "$demo" --native out/native || status=$?
  summary="The page in Chromium (AOT: $aot): $([[ $status == 0 ]] && echo "picked, on two workers, the demo paused and resumed and stopped while paused, the downloaded CSVs the native run's" || echo "FAILED")."
  echo "$summary"
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then echo "$summary" >> "$GITHUB_STEP_SUMMARY"; fi
  if [[ $status != 0 ]]; then exit 1; fi
fi

if [[ $replace == true ]]; then
  # FastYuv from shotdetector's tag of the packaged version, built as its README says, with its converter's constructor
  # made to throw (tools/replace-check.mjs's mark); the App rebuilt against it (README, "Licensing").
  version=$(sed -n 's/.*Include="ShotDetector.FastYuv" Version="\([^"]*\)".*/\1/p' Directory.Packages.props)
  dotnet restore src/ShotDetectorWeb.Core -v q -nologo
  frameReader=$(grep -o '"FrameReader/[^"]*"' src/ShotDetectorWeb.Core/obj/project.assets.json | head -1 | tr -d '"' | cut -d/ -f2)
  work=$(mktemp -d)
  trap 'rm -rf "$work"' EXIT
  git clone -q --depth 1 --branch "v$version" https://github.com/bisforboman/shotdetector "$work/shotdetector"
  python3 -I - "$work/shotdetector/src/ShotDetector.FastYuv/SwscaleYuv420.cs" <<'PY'
import sys
path = sys.argv[1]
text = open(path).read()
ctor = "    public SwscaleYuv420() : this(5, fullRange: false)\n    {\n    }"
if text.count(ctor) != 1:
    sys.exit(f"{path}: the parameterless constructor isn't where this check expects it.")
open(path, "w").write(text.replace(ctor, ctor[:-6] + '    throw new InvalidOperationException("This is the replaced ShotDetector.FastYuv.");\n    }'))
PY
  dotnet build "$work/shotdetector/src/ShotDetector.FastYuv" -c Release -v q -nologo \
    -p:ShotDetectorVersion="$version" -p:FrameReaderVersion="$frameReader"
  dll=$work/shotdetector/src/ShotDetector.FastYuv/bin/Release/net10.0/ShotDetector.FastYuv.dll
  publish=src/ShotDetectorWeb.App/bin/Release/net10.0/publish
  rm -rf "$publish"
  dotnet publish src/ShotDetectorWeb.App -c Release -v q -nologo -warnaserror -p:RunAOTCompilation=$aot -p:FastYuvAssembly="$dll"
  status=0
  node tools/replace-check.mjs "$publish/wwwroot" "${samples[0]}" | { grep -v '^MONO_WASM:' || true; } || status=$?
  # The next publish is the package's again (and not this bundle by accident).
  rm -rf "$publish"
  summary="FastYuv replaced from its source (v$version, AOT: $aot): $([[ $status == 0 ]] && echo "the rebuilt bundle runs it" || echo "FAILED")."
  echo "$summary"
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then echo "$summary" >> "$GITHUB_STEP_SUMMARY"; fi
  if [[ $status != 0 ]]; then exit 1; fi
fi
