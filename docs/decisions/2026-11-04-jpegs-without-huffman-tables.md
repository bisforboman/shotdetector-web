# An AVI's JPEGs without Huffman tables (2026-11-04)

The owner's queue of 2026-11-04 (through the coordinator): the Maybe row "An AVI's JPEGs without Huffman tables". An
MJPEG chunk may leave out its DHT segment (the AVI1 convention: the standard tables of the JPEG standard's Annex K
implied). The row assumed a browser's JPEG decoder doesn't supply them, so the page's picture and stills of such a file
would be missing, and planned the page inserting the standard tables before the first SOS.

## What there is

- A sample made as the row says: ffmpeg's MJPEG encoder with `-huffman default` (the standard tables; its default,
  `optimal`, writes tables of its own, which can't be left out), every DHT cut from each JPEG, muxed into AVI again
  (`-c:v copy`): 114 frames at 25 fps, no DHT in the file. FFmpeg decodes it to the same frames as the JPEGs with their
  tables (framemd5).
- ManagedCodecs decodes it too: NativeCheck on it "All agree" with FFmpeg (shots, CSVs, picture measures).
- **Chromium shows such a JPEG** as it shows the same JPEG with its tables (an `<img>`, `createImageBitmap`, the same
  pixels): libjpeg-turbo, which Chromium and Firefox decode JPEGs with, sets the standard tables when a JPEG has none
  ("Motion JPEG frames typically do not include the Huffman tables", `jdhuff.c`). Firefox isn't in a cloud session
  (Playwright has none there); Safari decodes with Apple's ImageIO, which nothing here can run.

So the case the row was for doesn't happen in Chromium, and likely not in Firefox.

## Question: what the row does now

**A. The sample and the checks, no change to the page (recommended).** `make-samples.sh` makes the sample (checked:
no DHT, standard tables' frames), the native and wasm checks run it, and the page check shows its picture and stills in
Chromium (and Firefox, where CI has it). Inserting tables waits for a browser shown to need it.

**B. As A, and the page inserts the standard tables anyway** before the first SOS of a JPEG without a DHT (bytes
copied, no decoding), for browsers nothing here checks (Safari).

**C. Close the row.** No sample: noted in backlog-done as not needed.

## Answer

The owner, 2026-11-04, through AskUserQuestion: **A**, the sample and the checks, no change to the page.
