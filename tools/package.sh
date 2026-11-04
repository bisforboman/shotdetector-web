#!/usr/bin/env bash
# The npm package (decision docs/decisions/2026-11-02-npm-package.md): package/'s own files (index.js, index.d.ts,
# package.json, README.md) and the App's publishes, put together into <out> (default out/package/), ready for npm pack:
#   aot/          the App's worker.js, blob-read.js, blob-write.js, pause.js, video-decode.js and _framework/, compiled
#                 ahead of time
#   interpreted/  the same, interpreted
#   threads.js, pause.js, blob-write.js, video-decode.js, threads/_framework/   the threaded runner (its WebM decoder
#                 imports too) and the threaded AOT bundle
#   licenses/     FastYuv's COPYING.LGPL (LGPL-2.1: it stays its own file), LICENSE, THIRD-PARTY-NOTICES.md
# package.json's "shotdetector" field gets the packages' versions from Directory.Packages.props.
#
# Usage: tools/package.sh [--out <dir>] [--aot-from <wwwroot>] [-p:Property=value...]
#   --aot-from: an AOT publish's wwwroot (tools/check.sh's publish_app, with its threads/_framework) to take the AOT and
#   threaded bundles from instead of publishing them again; only the interpreted bundle is published then.
#   -p:...: properties for every publish; -p:FastYuvAssembly=<dll> packs a FastYuv built from its source (README,
#   "Licensing"), the package's replaceability. Needs the wasm-tools workload.
set -euo pipefail
cd "$(dirname "$0")/.."

out=out/package aotFrom='' props=()
while [[ $# -gt 0 ]]; do
  case $1 in
    --out) out=$2; shift 2 ;;
    --aot-from) aotFrom=$2; shift 2 ;;
    -p:*) props+=("$1"); shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

app=src/ShotDetectorWeb.App
work=out/package-publish
# One publish into $work/<name>; obj/ and bin/ go before each (an AOT build's stripped IL: CLAUDE.md, Gotchas).
publish() {
  local name=$1; shift
  rm -rf "$app/obj" "$app/bin" "$work/$name"
  dotnet publish "$app" -c Release -v q -nologo -warnaserror -o "$work/$name" "$@" "${props[@]}"
  rm -rf "$app/obj" "$app/bin"
}

rm -rf "$out" "$work"
mkdir -p "$out/licenses" "$work"
if [[ -n $aotFrom ]]; then
  # Copied first: the publishes below delete the App's bin/, where tools/check.sh's publish is.
  [[ -d $aotFrom/_framework && -d $aotFrom/threads/_framework ]] || { echo "--aot-from $aotFrom: no _framework/ and threads/_framework/ there." >&2; exit 1; }
  mkdir -p "$work/aot-from"
  cp -r "$aotFrom/." "$work/aot-from/"
  aotRoot=$work/aot-from threadsRoot=$work/aot-from/threads
fi
publish interpreted -p:RunAOTCompilation=false
if [[ -z $aotFrom ]]; then
  publish aot -p:RunAOTCompilation=true
  publish threads -p:RunAOTCompilation=true -p:WasmEnableThreads=true
  aotRoot=$work/aot/wwwroot threadsRoot=$work/threads/wwwroot
fi

worker=(worker.js blob-read.js blob-write.js pause.js video-decode.js)
for bundle in aot interpreted; do
  root=$([[ $bundle == aot ]] && echo "$aotRoot" || echo "$work/interpreted/wwwroot")
  mkdir -p "$out/$bundle"
  for f in "${worker[@]}"; do cp "$root/$f" "$out/$bundle/"; done
  cp -r "$root/_framework" "$out/$bundle/_framework"
done
www=$work/interpreted/wwwroot
cp "$www/threads.js" "$www/pause.js" "$www/blob-write.js" "$www/video-decode.js" "$out/"
mkdir -p "$out/threads"
cp -r "$threadsRoot/_framework" "$out/threads/_framework"
cp -r "$work/interpreted/licenses/." "$out/licenses/"
cp LICENSE THIRD-PARTY-NOTICES.md "$out/licenses/"
cp package/index.js package/index.d.ts package/README.md "$out/"

# A bundle without FastYuv as its own file, or without its licence, isn't the package (LGPL-2.1).
for d in aot/_framework interpreted/_framework threads/_framework; do
  [[ -f $out/$d/ShotDetector.FastYuv.wasm ]] || { echo "No ShotDetector.FastYuv.wasm in $out/$d." >&2; exit 1; }
done
[[ -f $out/licenses/ShotDetector.FastYuv/COPYING.LGPL ]] || { echo "No FastYuv COPYING.LGPL in $out/licenses." >&2; exit 1; }

versions=$(sed -n 's/.*Include="\([^"]*\)" Version="\([^"]*\)".*/\1 \2/p' Directory.Packages.props)
node -e '
const fs = require("fs");
const [from, to, list] = process.argv.slice(1);
const pkg = JSON.parse(fs.readFileSync(from, "utf8"));
pkg.shotdetector = Object.fromEntries(list.trim().split("\n").map(l => l.split(" ")));
fs.writeFileSync(to, JSON.stringify(pkg, null, 2) + "\n");
' package/package.json "$out/package.json" "$versions"
rm -rf "$work"
echo "The package in $out: $(du -sh "$out" | cut -f1)."
