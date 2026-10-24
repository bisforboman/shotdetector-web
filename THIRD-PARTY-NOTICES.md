# Third-party notices

What the app bundles, each as its own assembly (DLL; on WebAssembly a `.wasm` file under `_framework/`), and under
which license:

| Component | Where from | License |
| --- | --- | --- |
| ShotDetector, FrameReader | NuGet (`ShotDetector`, `FrameReader`), [bisforboman/shotdetector](https://github.com/bisforboman/shotdetector) | MIT |
| ShotDetector.FastYuv | NuGet (`ShotDetector.FastYuv`), same repository | **LGPL-2.1-or-later** (a port of FFmpeg's libswscale conversion) |
| FFmpeg.AutoGen | NuGet, a dependency of ShotDetector (bindings only; unused here, and no FFmpeg library ships) | MIT |
| ManagedCodecs (Core, Containers, H264) | [bisforboman/ManagedCodecs](https://github.com/bisforboman/ManagedCodecs), submodule `external/ManagedCodecs` | not yet declared upstream (no LICENSE file at the pinned commit) |
| .NET runtime and libraries | Microsoft | MIT |

**ShotDetector.FastYuv (LGPL-2.1-or-later).** It stays a separate, replaceable assembly: nothing links it into another
(no single-file or merged AOT output; on WebAssembly, `ShotDetector.FastYuv.wasm` is its own file in the bundle and can
be swapped for a build of its source, which is in the shotdetector repository at the package's version). Its license
text ships next to it: the build copies the package's `COPYING.LGPL` to `licenses/ShotDetector.FastYuv/COPYING.LGPL`
in every output and publish folder.

FFmpeg.AutoGen (MIT, Ruslan Balanukhin; LICENSE.txt in its package) comes in because
ShotDetector's package references it; the trimmer keeps its assembly in the WebAssembly bundle, but nothing on this
path uses it (tools/app-check.mjs checks that the interpreted runtime never loads it).
