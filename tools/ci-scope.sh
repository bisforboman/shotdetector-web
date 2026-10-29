#!/usr/bin/env bash
# Which checks a pull request's changes need (README, "Checks"; ci.yml's step "What the change touches"): reads the
# changed paths on standard input, one a line, and prints the widest tier any of them needs:
#   none    nothing a check can see: docs/, *.md, LICENSE, and the WebCodecs measurement (tools/webcodecs/,
#           tools/WebCodecsCheck/, tools/WebCodecsWasm/), which no check builds
#   native  the native check alone: the page's own files that no wasm check loads (index.html, style.css, app.js, which
#           NativeCheck's DefaultsCheck reads, timeline.js, versions.js, worker.js, panels/) and tools/NativeCheck/
#   all     everything else: the core, the App's .NET side, blob-read.js and pause.js (the wasm checks import them from
#           the bundle), the packages, the samples' recipe, the checks' scripts, the workflows, and any path not named
#           here. A wasm check always needs the native one: it compares the native run's CSVs.
# A path no rule names is "all": a new file is checked in full until a rule says otherwise.
set -euo pipefail

tier=none
while IFS= read -r path; do
  [[ -z $path ]] && continue
  case $path in
    docs/* | *.md | LICENSE | tools/webcodecs/* | tools/WebCodecsCheck/* | tools/WebCodecsWasm/*)
      need=none ;;
    src/ShotDetectorWeb.App/wwwroot/blob-read.js | src/ShotDetectorWeb.App/wwwroot/pause.js)
      need=all ;;
    src/ShotDetectorWeb.App/wwwroot/index.html | src/ShotDetectorWeb.App/wwwroot/style.css | \
    src/ShotDetectorWeb.App/wwwroot/app.js | src/ShotDetectorWeb.App/wwwroot/timeline.js | \
    src/ShotDetectorWeb.App/wwwroot/versions.js | src/ShotDetectorWeb.App/wwwroot/worker.js | \
    src/ShotDetectorWeb.App/wwwroot/panels/* | tools/NativeCheck/*)
      need=native ;;
    *)
      need=all ;;
  esac
  echo "$need  $path" >&2
  if [[ $need == all ]]; then tier=all
  elif [[ $need == native && $tier == none ]]; then tier=native
  fi
done
echo "$tier"
