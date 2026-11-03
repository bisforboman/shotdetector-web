#!/bin/bash
# Cloud sessions only: what CLAUDE.md's "Before pushing" needs and the container lacks. The .NET 10 SDK, the wasm-tools
# workload (wasm, page, replace), the restored core and NativeCheck, BtbN's FFmpeg 8.1 shared build (pinned in
# .github/actions/btbn-ffmpeg, SHA-256 checked, kept in ~/.cache/btbn: SHOTDETECTOR_FFMPEG_LIBS, LD_LIBRARY_PATH, and its
# ffmpeg with libx264 first on PATH for tools/make-samples.sh), and Playwright for the page check (the container's own
# when it is the version tools/page-check pins). Idempotent: the container is cached after it runs, so a later start
# finds everything in place and only checks. A failed optional step is logged and the session starts all the same.
# Left out: the samples (tools/make-samples.sh; SINTEL needs Sintel's trailer from download.blender.org).
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR" || exit 0
note() { echo "session-start: $*"; }
env_line() { [ -n "${CLAUDE_ENV_FILE:-}" ] && echo "$1" >> "$CLAUDE_ENV_FILE"; true; }

# Ubuntu's own package: the cloud network policy blocks builds.dotnet.microsoft.com (dotnet-install.sh) but not the
# Ubuntu archive.
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  { apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0; } \
    || note "the .NET 10 SDK didn't install"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
env_line "export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1"

mono=Microsoft.NETCore.App.Runtime.Mono.browser-wasm
# The workload's record can outlive its packs (CLAUDE.md, Gotchas: NETSDK1147), so look for a pack as well.
if ! dotnet workload list 2>/dev/null | grep -q '^wasm-tools ' \
  || ! { [ -d "$HOME/.dotnet/packs/$mono" ] || [ -d "/usr/lib/dotnet/packs/$mono" ]; }; then
  dotnet workload install wasm-tools >/dev/null || note "the wasm-tools workload didn't install"
fi

# The App needs the workload to restore; tools/check.sh restores it on its first publish.
dotnet restore src/ShotDetectorWeb.Core -v q || note "src/ShotDetectorWeb.Core didn't restore"
dotnet restore tools/NativeCheck -v q || note "tools/NativeCheck didn't restore"

# BtbN's gpl-shared FFmpeg 8.1 for linux64: the pin is read from the action, so it moves with CI's.
action=.github/actions/btbn-ffmpeg/action.yml
release=$(sed -n 's/^ *release=\([^ ]*\)$/\1/p' "$action")
tag=$(sed -n 's/^ *8\.1) tag=\([^ ]*\) ;;$/\1/p' "$action")
name="ffmpeg-$tag-linux64-gpl-shared-8.1"
sum=$(sed -n "s/^ *$name) sum=\([0-9a-f]*\) ;;\$/\1/p" "$action")
dir="$HOME/.cache/btbn/$name"
if [ -z "$release" ] || [ -z "$tag" ] || [ -z "$sum" ]; then
  note "no FFmpeg 8.1 pin found in $action: set SHOTDETECTOR_FFMPEG_LIBS by hand (CLAUDE.md)"
elif [ ! -x "$dir/bin/ffmpeg" ] && [ "$(uname -m)" = x86_64 ]; then
  tmp=$(mktemp -d)
  if curl -fsSL --retry 3 --connect-timeout 30 -o "$tmp/a.tar.xz" \
      "https://github.com/BtbN/FFmpeg-Builds/releases/download/$release/$name.tar.xz" \
    && [ "$(sha256sum < "$tmp/a.tar.xz" | cut -d' ' -f1)" = "$sum" ] \
    && tar -xJf "$tmp/a.tar.xz" -C "$tmp"; then
    # The libraries beside the executables, one folder to load both from (as the action does).
    cp -a "$tmp/$name"/lib/*.so* "$tmp/$name/bin/"
    mkdir -p "$(dirname "$dir")" && rm -rf "$dir" && mv "$tmp/$name" "$dir"
  else
    note "BtbN's FFmpeg 8.1 didn't download or didn't match its SHA-256: the native check needs SHOTDETECTOR_FFMPEG_LIBS"
  fi
  rm -rf "$tmp"
fi
if [ -x "$dir/bin/ffmpeg" ]; then
  env_line "export SHOTDETECTOR_FFMPEG_LIBS=\"$dir/bin\" LD_LIBRARY_PATH=\"$dir/bin\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}\" PATH=\"$dir/bin:\$PATH\""
fi

# Playwright: page-check.mjs takes tools/page-check's node_modules, else whatever require finds. The container's own
# (with its browsers) does when it is the pinned version; otherwise npm ci (the browsers then: npx playwright install).
pin=$(sed -n 's/.*"playwright": "\([^"]*\)".*/\1/p' tools/page-check/package.json)
have=$(cd tools/page-check && node -e "console.log(require('playwright/package.json').version)" 2>/dev/null)
if [ "$have" != "$pin" ]; then
  (cd tools/page-check && npm ci --no-audit --no-fund >/dev/null) || note "tools/page-check's npm ci failed"
  note "Playwright $pin's browsers: npx playwright install chromium firefox in tools/page-check"
elif ! ls -d "${PLAYWRIGHT_BROWSERS_PATH:-$HOME/.cache/ms-playwright}"/firefox-* >/dev/null 2>&1; then
  env_line "export PAGE_CHECK_FIREFOX=skip"
  note "Playwright has no Firefox here: PAGE_CHECK_FIREFOX=skip leaves the page check's Firefox run out"
fi

if [ ! -d samples ]; then
  note "no samples/: tools/make-samples.sh (SINTEL=<sintel_trailer-1080p.mp4> for the trailer; download.blender.org)"
fi
exit 0
