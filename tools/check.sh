#!/usr/bin/env bash
# The exactness checks (README, "Checks"):
#   native: ShotDetectorWeb.Core (ManagedCodecs + FastYuv) against ShotDetector's Detect(path) through FFmpeg in-process,
#           every detector, scene and stats CSVs byte for byte (tools/NativeCheck);
#   wasm:   the same library on browser-wasm under Node, through the page's own exports (src/ShotDetectorWeb.App,
#           driven by tools/app-check.mjs), its CSVs against the native run's;
#   replace: LGPL-2.1, FastYuv replaceable: one built from its source at the package's tag, with a visible change, in a
#           bundle rebuilt with -p:FastYuvAssembly, must be the one that runs (tools/replace-check.mjs).
#
# Usage: tools/check.sh [native] [wasm] [replace] [aot]
#   (default: native wasm; aot: the wasm and replace bundles compiled ahead of time)
#   Needs samples/ (tools/make-samples.sh), SHOTDETECTOR_FFMPEG_LIBS (an FFmpeg 8.1 shared build's folder, also on
#   LD_LIBRARY_PATH) for native, Node plus the wasm-tools workload (dotnet workload install wasm-tools) for wasm and
#   replace, and git with access to github.com for replace.
set -euo pipefail
cd "$(dirname "$0")/.."

native=false wasm=false replace=false aot=false
for arg in "$@"; do
  case $arg in
    native) native=true ;;
    wasm) wasm=true ;;
    replace) replace=true ;;
    aot) aot=true ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done
if [[ $native == false && $wasm == false && $replace == false ]]; then native=true wasm=true; fi

shopt -s nullglob
samples=(samples/*.mp4)
rejects=(samples/reject/*)
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
  node tools/app-check.mjs "$publish/wwwroot" out/wasm "${samples[@]}" --reject "${rejects[@]}" 2> out/wasm-stderr.txt \
    | { grep -v '^MONO_WASM:' || true; } || status=$?
  cat out/wasm-stderr.txt
  if [[ $status != 0 ]]; then echo "The WebAssembly run failed (above)." >&2; exit 1; fi
  # Interpreted, FFmpeg.AutoGen must not be loaded at all; AOT loads some assemblies before the first call, FFmpeg.AutoGen
  # among them, so there only the runs must not load it.
  if [[ $aot == true ]]; then expect='^No FFmpeg assembly loaded'; else expect='^No FFmpeg assembly loaded at all\.$'; fi
  if ! grep -qE "$expect" out/wasm-stderr.txt; then echo "The WebAssembly run failed or loaded FFmpeg (above)." >&2; exit 1; fi

  files=0 differ=0 extra=0
  for n in out/native/*.csv; do
    [[ $n == *.ffmpeg.* ]] && continue
    files=$((files + 1))
    w=out/wasm/${n#out/native/}
    if ! cmp -s "$n" "$w"; then echo "Differs: $w"; differ=$((differ + 1)); fi
  done
  for w in out/wasm/*.csv; do
    if [[ ! -f out/native/${w#out/wasm/} ]]; then echo "Not written natively: $w"; extra=$((extra + 1)); fi
  done
  summary="WebAssembly (AOT: $aot) and native: $((files - differ)) of $files CSVs identical."
  echo "$summary"
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then echo "$summary" >> "$GITHUB_STEP_SUMMARY"; fi
  if [[ $differ != 0 || $extra != 0 ]]; then exit 1; fi
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
