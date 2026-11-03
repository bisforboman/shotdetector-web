# A damaged JPEG in an MJPEG AVI

Date: 2026-11-02. Status: answered, A (the owner, 2026-11-03).

## The question

The row "ManagedCodecs 0.6.0 and FrameReader 2.21.0, with the AVI scope widened" asks for damaged images decoded as
FFmpeg does (`VideoFrame.IsDamaged`). ManagedCodecs 0.6.0's `MjpegDecoder` decodes a JPEG whose scan runs past its data
(a truncated image, a common damage in camera and capture files) instead of throwing: what the scan did not reach is 0,
and the frame says `IsDamaged`. What should the core do with such a frame?

## What FFmpeg does

FFmpeg's MJPEG decoder logs `overread` and returns the frame. It doesn't fill the part the scan didn't reach: that part is
whatever the frame's buffer held before, a frame from its buffer pool. Which one depends on the decoder's threads. In a
320x240 sample whose 40th JPEG has its scan cut halfway (an EOI there), measured 2026-11-02 with the pinned FFmpeg 8.1:

| Decoder | Frame 39's lower part |
| --- | --- |
| `ffmpeg -threads 1` | frame 37's picture |
| `ffmpeg -threads 4` | frame 36's picture |
| ManagedCodecs 0.6.0 | 0 (black luma, and green where the chroma is 0) |

So FFmpeg's own result for that frame is not one picture, and no decoder can equal it byte for byte. Everything else
is the same: NativeCheck on that sample against ShotDetector's FFmpeg path (2026-11-02) gives the same scenes CSV, shots,
timeline files and frame types, the same packets. The stats CSV differs only in the rows that read frame 39: frame
numbers 38 to 43, the adaptive ratio's window around it. The picture's measures differ for frame 39 only.

The other parts of the row don't depend on this. The other AVI FourCCs FFmpeg reads as MJPEG (`dmb1` checked) are the
same byte for byte. 4:1:1 and 4:4:0 JPEG decode, but ShotDetector 3.27.0's frame source takes neither, so they stay
refused, each with its own message.

## Options

- **A. Decode it as ManagedCodecs does, and say so.** The frame goes to ShotDetector with the unreached part 0. That
  part is the same on every run, thread count and wasm. The result's warnings name the damaged frames: "Frame 40's JPEG
  ends before its picture does (damaged): FFmpeg fills the rest from an earlier frame, which one depending on its
  threads, so the stats around it can't be compared with FFmpeg's." NativeCheck checks a damaged sample's CSVs and
  measures equal the FFmpeg path's everywhere but the rows that read a damaged frame, and those rows the same on one
  and four threads and on wasm.
- **B. Refuse the file when a damaged frame is met.** An `UnsupportedVideoException` in the run when the decoder gives
  one ("Frame 40's JPEG is damaged: ..."). The page shows the message and gives no result, as for any refused file.
  Exactness is kept for everything the page shows, but a file with one bad frame gives nothing at all.
- **C. A's decoding, without the warning.** The same frames as A, nothing said. The page's results would differ from
  FFmpeg's around the frame without saying why.

## Recommendation

A. FFmpeg's picture there is undefined, so B refuses a file over a difference that no decoder can avoid. A keeps every
figure that can be exact exact, checks it, and says where the rest differ and why.

## Answer

A, the owner's choice through AskUserQuestion on 2026-11-03: decode it as ManagedCodecs does, name the damaged
frames in the run's warnings, and hold everything else to the FFmpeg path's exactly.
