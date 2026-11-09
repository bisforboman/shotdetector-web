

- The reference tone left out of the detection
  - Why: ShotDetector 3.25.0's `YuvVideo.ReferenceTone` and `DetectionOptions.TrimReferenceTone` leave a file's
    leading tone (and the bars over it) out of the shots, as `Detect(path)` does with FFmpeg finding the tone; the page
    measures the tone already (`Mp4Audio`, the sound's worker), alongside the run, not before it.
  - Where: decision [2026-11-04-reference-tone.md](decisions/2026-11-04-reference-tone.md), the owner's 1A and 2A:
    ShotDetector's ranges taking the trim first (3.28.0's is "not for ranges": `ScoreRange` refuses a time range),
    then an option on the page, off by default, the run waiting for the sound's worker's tone, `YuvVideo.ReferenceTone`
    set from it in `Api`; NativeCheck's run with `TrimReferenceTone` on the tone sample, whole, split and resumed.
  - Done when: the native and wasm checks pass on the tone sample with the option on, byte for byte.
  - Status: Done (2026-11-09, the owner's choice through AskUserQuestion): the page's Reference tone option (keep,
    the default, or leave out); left out, the run waits for the sound's tone and sends it (`DetectRequest.Tone`), the
    App sets `YuvVideo.ReferenceTone` on every export that reads a request; the answer's `frameCount` is the frames
    read; a Stop in the leading tone held past it (`StopsFrom`, an estimate until shotdetector#770's public
    `ToneStart`: Waiting, "A stop in the leading reference tone, exactly"). A sample, `tone-cuts-320x240-25.mp4`;
    NativeCheck's `ToneCheck` against `Detect(path)` whole, split, stopped and resumed: "All agree" on every sample (54);
    AOT wasm 5328 of 5328 files identical; the page check in Chromium passed, the tone sample with the tone left out on
    threads and on two workers (locally on the container's Chromium 1243 under Playwright 1.65.0, Firefox left out)
