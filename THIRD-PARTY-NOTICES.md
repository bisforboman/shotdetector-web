# Third-party notices

What the app bundles, each as its own assembly (DLL; on WebAssembly a `.wasm` file under `_framework/`), and under
which license:

| Component | Where from | License |
| --- | --- | --- |
| ShotDetector, FrameReader | NuGet (`ShotDetector`, `FrameReader`), [bisforboman/shotdetector](https://github.com/bisforboman/shotdetector) | MIT |
| ShotDetector.FastYuv | NuGet (`ShotDetector.FastYuv`), same repository | **LGPL-2.1-or-later** (a port of FFmpeg's libswscale conversion) |
| FFmpeg.AutoGen | NuGet, a dependency of ShotDetector (bindings only; unused here, and no FFmpeg library ships) | MIT |
| ManagedCodecs (Core, Containers, H264, Aac) | NuGet (`ManagedCodecs.Containers`, `ManagedCodecs.H264`, `ManagedCodecs.Aac`, `ManagedCodecs.Core`), [bisforboman/ManagedCodecs](https://github.com/bisforboman/ManagedCodecs) | MIT |
| .NET runtime and libraries | Microsoft | MIT |

**ShotDetector.FastYuv (LGPL-2.1-or-later).** It stays a separate assembly (no single-file output; on WebAssembly,
`ShotDetector.FastYuv.wasm` is its own file in the bundle), and its license text ships next to it: the build copies the
package's `COPYING.LGPL` to `licenses/ShotDetector.FastYuv/COPYING.LGPL` in every output and publish folder. Its
source is in the shotdetector repository at the package's version, and this app's whole source is here (MIT), so it
can be replaced by building a changed FastYuv and publishing the app against it (`-p:FastYuvAssembly`, README
"Licensing"; CI proves it with `tools/check.sh replace aot`). The published bundle's file can't be swapped on its own:
it is trimmed, `dotnet.js` holds each file's hash, and compiled ahead of time, as the page is, FastYuv's code is also
in `dotnet.native.wasm` ([docs/decisions/2026-10-24-fastyuv-replaceable.md](docs/decisions/2026-10-24-fastyuv-replaceable.md)).

FFmpeg.AutoGen (MIT, Ruslan Balanukhin; LICENSE.txt in its package) comes in because
ShotDetector's package references it; the trimmer keeps part of it in the WebAssembly bundle (ShotDetector's
frame-source path shares code with its FFmpeg path, so it is reachable), but nothing on this path runs it (tools/app-check.mjs checks that the interpreted runtime never loads it).

## Sintel's trailer

The checks run on, and the page's demo ("Try the demo", served as `demo/sintel-trailer.mp4`) runs, a re-encode of the
trailer of *Sintel*: © copyright Blender Foundation | [durian.blender.org](https://durian.blender.org), licensed
under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/). Changes: scaled to 854x480 and
re-encoded to H.264 and AAC (`tools/make-samples.sh`, from `sintel_trailer-1080p.mp4` on download.blender.org).
Neither the original nor the re-encode is in this repository: CI and the Pages deploy each download the original
(pinned by SHA-256) and re-encode it. The page credits it next to the button.
