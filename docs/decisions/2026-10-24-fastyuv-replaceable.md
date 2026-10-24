# FastYuv on the page: how it stays replaceable (2026-10-24)

ShotDetector.FastYuv is LGPL-2.1-or-later. The owner's earlier decision (shotdetector's
[docs/decisions/2026-10-24-managedcodecs-showcase.md](https://github.com/bisforboman/shotdetector/blob/main/docs/decisions/2026-10-24-managedcodecs-showcase.md))
keeps it its own DLL, and README "Licensing" said a visitor could swap the bundle's `ShotDetector.FastYuv.wasm` for a
build of its source. The page ships compiled ahead of time ([2026-10-24-the-page.md](2026-10-24-the-page.md),
"Delivery"); this decides what "replaceable" means there. Backlog row "FastYuv under AOT".

## What a swap of the published file does (tried 2026-10-24)

A FastYuv built from shotdetector's `v3.14.0` with its converter's constructor made to throw, converted to the
bundle's format (Webcil) by the same SDK, put in place of `_framework/ShotDetector.FastYuv.wasm` in the App's
published bundle, then `Open` and `Detect` on a sample:

| Bundle | File swapped | Also its sha256 in `dotnet.js` | Result |
| --- | --- | --- | --- |
| interpreted, Chromium | yes | no | the runtime doesn't start: the download fails its integrity check (`dotnet.js` inlines the boot config, a sha256 per file, fetched with `integrity`) |
| interpreted, Chromium or Node | yes | yes | `Could not load file or assembly 'System.Runtime'`: the bundle is trimmed, its assemblies refer to the implementation assemblies directly and the facades are gone, while a normal build refers to `System.Runtime` |
| AOT, Node | yes | yes | the runtime aborts at start (`aot-runtime.c`): FastYuv's code is compiled into `dotnet.native.wasm` (`mono_aot_ShotDetector_FastYuv_*`) for the assembly it was built from, and AOT loads FastYuv before the first call |

`ShotDetector.FastYuv.wasm` is its own file in both builds (the AOT one keeps its IL; the two files are identical), but
on its own it can't be replaced: trimming, the boot config's hashes and the AOT image each tie the bundle to the
assembly it was published with.

## Question

How does the page keep FastYuv replaceable, as LGPL-2.1 (section 6) asks of a work that uses the library?

## Choices

1. **Rebuild with your FastYuv, keep AOT.** The app's whole source is public (MIT), FastYuv's is public at the
   package's tag: a user changes FastYuv, builds it, and publishes the App against that build (one property,
   `-p:FastYuvAssembly=<dll>`); the publish trims, compiles and hashes it as it does the package's. Section 6(a)'s
   route: the work that uses the library, in source, so the user can relink. A check builds a visibly changed FastYuv
   from its tag and proves the AOT bundle runs it.
2. **A drop-in file, shipped interpreted**: no AOT, FastYuv's references kept (facades in the bundle, or no trimming),
   and a documented hash update in `dotnet.js`. Detection is about 4 to 5 times slower (CI, 2026-10-24: 720p sample,
   4.5 s per run interpreted, 1.0 s AOT) and the bundle bigger, against the bundle size row.
3. **A mixed build**: AOT for everything but FastYuv, if Mono's wasm AOT can leave one assembly out, plus the facades
   and the hash. Unverified; the conversion, the hot loop, would run interpreted; a swap still needs the hash edit.

## Answer

**1** (the owner, 2026-10-24). The page stays fast, the recipe is one command on top of the normal build, and CI
proves it: `tools/check.sh replace aot` (README, "Checks"). README "Licensing" and THIRD-PARTY-NOTICES.md give the
recipe and no longer say the published file can be swapped by itself. `ShotDetector.FastYuv.wasm` stays its own file in
`_framework/` and `licenses/ShotDetector.FastYuv/COPYING.LGPL` ships, as before.
