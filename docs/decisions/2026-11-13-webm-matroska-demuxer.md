# WebM on ManagedCodecs' Matroska demuxer (2026-11-13)

The Maybe row "WebM on ManagedCodecs' Matroska demuxer". The core reads WebM with its own `MatroskaReader` (the owner's
choice of 2026-11-02, [2026-11-02-webm-browser-decoder.md](2026-11-02-webm-browser-decoder.md): "a Matroska demuxer in
ManagedCodecs would replace the reader later"). ManagedCodecs has had `MatroskaDemuxer` since 0.8.0, and another session
moves the repository to 0.13.0 (branch `ccr-d0d88369-sh5umk`). This decision assesses 0.13.0's demuxer against what
`WebmInput` needs; no code changes with it.

## What WebmInput needs, and what the demuxer gives

Read from ManagedCodecs' source at `v0.13.0` (`src/ManagedCodecs.Containers/Matroska/MatroskaDemuxer.cs`), and measured:
a scratch program (outside the repository) opened every WebM sample `tools/make-samples.sh` makes and the two WebM
refusals with both the core's `MatroskaReader` and `MatroskaDemuxer` 0.13.0, and compared what each gives.

| WebmInput needs | `MatroskaReader` | `MatroskaDemuxer` 0.13.0 |
| --- | --- | --- |
| The video blocks' pts, sizes and keyframes, data not read (`VideoPackets`, as `TimingOnlyStream` gives an MP4's) | `Blocks`: headers only | `ReadPacketInfo()`: from the index built at opening, no read. **The same on all 10 samples and both refusals**, packet by packet |
| Each range's packets, from a keyframe | walks the blocks from the file's start, skipping those before the range | `Seek(pts)` on its index, then `ReadPackets()` |
| CodecPrivate (AV1's 'av1C') | yes | `VideoStreamInfo.CodecPrivate`: the same bytes |
| DefaultDuration, exactly (FFmpeg's average rate: `av_reduce(1e9, DefaultDuration, 30000)`) | yes, and the deprecated FrameRate standing for it, as FFmpeg | only as `FramesPerSecond` = 1e9 / DefaultDuration, a double: `Math.Round(1e9 / fps)` gives it back exactly (the same on every sample). **FrameRate isn't read**: a file with only that gets the demuxer's own measured rate |
| Without a DefaultDuration: FFmpeg's probed rate (`ProbedFrameRate`, decision 2026-11-05) over every track's packets (the 5 MB stop counts them all) and whether blocks have a BlockDuration | `Packets`: every track's blocks, with the flag | **No**: its rate is its own, (n − 1) / span (20.24 fps for `vfr-nodefault-320x240.webm`, where FFmpeg says r_frame_rate 25, no average; 29.9709 for `vp9-nodefault-320x240-2997.webm`, FFmpeg 30000/1001). `ProbedFrameRate` stays either way. Its packets cover the first video and audio track only (a third track's sizes are missing for the 5 MB stop); a BlockDuration shows as the packet's `Duration` |
| The Segment's Duration as written (FFmpeg: Duration × TimestampScale / 1000 µs, truncated) | the double | `Duration` rounded to whole ticks: the same on every sample (FFmpeg writes whole milliseconds), **not** for a fractional Duration (mkvmerge's can be) |
| No Duration (a live recording's) | null | 0: the same meaning |
| Time base, start pts | TimestampScale; the first block's | the same (pts minus a track's CodecDelay, as FFmpeg; video tracks have none) |
| Colour | the track's Colour read, but `WebmInput` takes the matrix, range and chroma location from VP9's frame header and AV1's sequence header, as FFmpeg's decoders say them | Colour as enums (same values); not needed either way |
| FlagInterlaced (refused when 1) | yes | **not read** |
| Projection (any yaw, pitch or roll refused) | yes | `Rotation` from a roll by a multiple of 90°; **0 for a yaw or pitch** (a mirror), so such a file would pass unrefused unless the core reads it |
| Laced video, header stripping, zlib (ContentEncodings) | refused | read (FFmpeg's behaviour, checked upstream packet by packet): a widening, but no sample or real-world file needs it |
| The audio track for the page's notice ("the audio is 'A_OPUS'") | `Audio` | `Audio` (codec ids, rate, channels); and it would let `Mp4Audio` decode AAC, MP3 or PCM in Matroska, which WebM doesn't allow |

So on every sample the demuxer gives the same packets and, with the double turned back into nanoseconds, the same
description inputs. What it lacks are edge cases the core reads today (FrameRate, FlagInterlaced, a projection's yaw and
pitch, a fractional Duration, a third track's sizes for the probe): replacing the reader means either keeping a header
read of the core's own for those or asking ManagedCodecs for them.

### Memory and reads on a large file

A two-hour WebM made from `av1-opus-320x240-25.webm` looped with `-c copy` (182,400 video frames, 360,001 Opus packets,
493 MB), natively (.NET 10, workstation GC, `GC.GetTotalAllocatedBytes` and `GC.GetTotalMemory(true)` around opening
and collecting the video's pts, sizes and flags into arrays as `VideoPackets.Builder` does), the stream behind a 1 MB
buffer as `BlobStream`'s; the same numbers on each of two runs:

| Opening | Allocated | Kept | Read from the file |
| --- | --- | --- | --- |
| `MatroskaReader` (`Read`, `Blocks`) | 8.8 MB | 4.5 MB (the packets' arrays) | 491 MB in 469 reads of 1 MB |
| `MatroskaDemuxer` (open, `ReadPacketInfo`), disposed | 189.7 MB | 4.5 MB | 674 MB in 643 reads |
| `MatroskaDemuxer`, kept open (as a range's reader would) | 189.7 MB | 39.2 MB | 674 MB |

The demuxer indexes every frame of both tracks in a `List` of 64-byte entries, then copies it to an array: 34.7 MB kept
for this file, and by the list's growth about 100 MB live at its peak (its last array, 1,048,576 entries, 67 MB, while
the 35 MB copy is made; worked out from the sizes, not measured). It has no track filter (`Mp4Demuxer` has
`MediaTypes`) and no columns (`Mp4Demuxer`'s index is 29 B a sample, README "What the heap holds"). Its 64 KB window
re-reads across the 1 MB buffer's edges (674 MB for a 493 MB file). Each opening does all of that: the description's,
then each range's and each worker's, every one reading the whole file. The core's reader reads the whole file at
opening too (its block headers are a few bytes apart, inside each 1 MB slice), but a range's walk stops at its range's
end.

On the page that is a worker's wasm heap growing by about 100 MB at each opening of a two-hour WebM, beside 4.5 MB
now, and the renderer reading the whole file once more per range: not a wall, but a step back from what "What the
heap holds" got the MP4 path down to.

### "WebM without a DefaultDuration or a Duration"

All three rows that one became are done (backlog-done: "WebM without a Duration", "WebM without a DefaultDuration",
"VP9 or AV1 in MP4"), on the core's reader. The demuxer would not have covered the DefaultDuration one (its rate isn't
FFmpeg's; `ProbedFrameRate` stays), it covers the Duration one (0 for none). The one refusal left there, blocks with
BlockDurations and no DefaultDuration (FFmpeg averages their durations), gets its durations from the demuxer as packet
durations, but FFmpeg's averaging is still to port either way.

## Question: the core's reader or the demuxer

**A. Keep `MatroskaReader` for now; ask ManagedCodecs for what the demuxer lacks (recommended).** The asks, one issue
on ManagedCodecs (this session can't reach that repository: the owner or its coordinator files it): an index of the
tracks asked for only and in columns, as `Mp4Demuxer`'s; DefaultDuration exactly (nanoseconds, or the rate as a
`Rational`); FlagInterlaced (and FieldOrder); the Projection's yaw and pitch (or a refusal of them); the Segment's
Duration as a double; and the other tracks' packet sizes (or a `ReadPacketInfo` of every track) for FFmpeg's probe.
The row becomes Waiting on that release, then `WebmInput` moves to the demuxer, held to the same CSVs on every WebM
sample, and `MatroskaReader` goes. Nothing changes for files now; the memory stays where it is.

**B. Replace the reader now.** `WebmInput` on `MatroskaDemuxer` 0.13.0, after the bump. The core keeps a header read of
its own for FrameRate, FlagInterlaced, the projection and the Duration (most of `MatroskaReader`'s track and info
parsing), and its probe without a DefaultDuration counts only the first video and audio track. Gains laced and
compressed video and `Seek`; costs about 100 MB of heap at each opening of a two-hour WebM, and a whole read of the
file for each range.

**C. Keep `MatroskaReader`; close the row.** The 2026-11-02 "replace later" dropped: the reader stays the core's.

## Answer

The owner, 2026-11-13, through AskUserQuestion: **A**, keep `MatroskaReader` for now and ask ManagedCodecs for what the
demuxer lacks. The row "WebM on ManagedCodecs' Matroska demuxer" is Waiting on that issue and its release; the asks:

1. `MatroskaDemuxer` indexing only the tracks asked for (as `Mp4Demuxer`'s `MediaTypes`), in columns rather than
   64-byte entries, built at its final size.
2. The video track's DefaultDuration in nanoseconds (or its rate as a `Rational`), and the deprecated FrameRate standing
   for it as in FFmpeg's matroskadec.
3. FlagInterlaced (and FieldOrder).
4. The Projection's yaw and pitch, or a refusal of a projection the demuxer can't express as `Rotation`.
5. The Segment's Duration as written (a double, in ticks).
6. Every track's packet sizes in file order (or `ReadPacketInfo` over every track), for FFmpeg's probe without a
   DefaultDuration.
