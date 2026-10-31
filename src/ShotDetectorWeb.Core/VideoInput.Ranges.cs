using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// A range of the video's frames, scored on a runtime of its own (a browser Web Worker) by
/// <see cref="VideoInput.ScoreRangeAsync"/>, its scores merged with the others' by <see cref="VideoInput.Merge"/>
/// (docs/decisions/2026-10-26-workers.md). <see cref="FirstFrame"/> is ShotDetector's <c>firstFrame</c>: 0 for the first
/// range, else one past a keyframe, which the range decodes from and gives as the frame before its first.
/// </summary>
/// <param name="FirstFrame">The range's first frame (0-based, display order).</param>
/// <param name="EndFrame">The frame after its last: the next range's first, or the video's frame count.</param>
/// <param name="Keyframe">The video packet (decode order, an index of <see cref="VideoInput.Packets"/>) it decodes from.</param>
public sealed record FrameRange(int FirstFrame, int EndFrame, int Keyframe);

/// <summary>
/// A stopped run's way on (<see cref="VideoInput.Continue"/>): the earlier ranges' scores to keep, in order, and the range
/// that scores the rest of the video (null when they reach its end). Merged (<see cref="VideoInput.Merge"/>), the earlier
/// ranges and the rest's give the whole video's result.
/// </summary>
public sealed record Continuation(IReadOnlyList<RangeScores> Earlier, FrameRange? Rest);

public abstract partial class VideoInput
{
    /// <summary>
    /// The frames the video shows: its packets but the discarded ones (an edit list's, decoded but not shown), which a
    /// whole run reads (<see cref="DetectionResult.FrameCount"/>).
    /// </summary>
    public int ShownFrames
    {
        get
        {
            int shown = 0;
            foreach (var flags in Packets.Flags)
            {
                if (!flags.HasFlag(PacketFlags.Discard))
                    shown++;
            }

            return shown;
        }
    }

    /// <summary>
    /// Splits the video into at most <paramref name="count"/> ranges about equal in frames, each after the first starting
    /// one past a keyframe (its frame before), the keyframe nearest to an equal split's start; fewer when the keyframes
    /// are too few (one range for a video with one keyframe). The same file gives the same ranges on every runtime, so a
    /// worker told the count and its index decodes the range the page planned. With <paramref name="from"/> (a run
    /// resumed: <see cref="Continue"/>'s range's first frame), the frames from there on are split the same way, the first
    /// range starting there.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="from"/> is neither 0 nor where a range can start.</exception>
    public IReadOnlyList<FrameRange> Ranges(int count, int from = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var (starts, frames) = RangeStarts();
        (int FirstFrame, int Keyframe) last = (0, 0);
        if (from != 0)
        {
            int at = starts.FindIndex(s => s.FirstFrame == from);
            if (at < 0)
                throw new ArgumentException($"A range can't start at frame {from}: it isn't one past a keyframe.", nameof(from));
            last = starts[at];
        }

        var ranges = new List<FrameRange>();
        int start = last.FirstFrame;
        starts.RemoveAll(s => s.FirstFrame <= start);
        for (int r = 1; r < count && starts.Count > 0; r++)
        {
            long target = start + (long)(frames - start) * r / count;
            var nearest = starts.MinBy(s => Math.Abs(s.FirstFrame - target));
            if (nearest.FirstFrame <= last.FirstFrame)
                continue;
            ranges.Add(new FrameRange(last.FirstFrame, nearest.FirstFrame, last.Keyframe));
            last = nearest;
        }

        ranges.Add(new FrameRange(last.FirstFrame, frames, last.Keyframe));
        return ranges;
    }

    /// <summary>
    /// The places a range can start, in frame order: one past each keyframe but the first frame's, before the last
    /// frame, each with its keyframe's packet (decode order); and the frames the video shows.
    /// </summary>
    (List<(int FirstFrame, int Keyframe)> Starts, int Frames) RangeStarts()
    {
        var packets = Packets;

        // Each shown frame's pts in display order: a keyframe's index there is its frame's.
        var shown = new List<long>(packets.Count);
        for (int i = 0; i < packets.Count; i++)
        {
            if (!packets.Flags[i].HasFlag(PacketFlags.Discard))
                shown.Add(packets.Pts[i]);
        }

        shown.Sort();
        int frames = shown.Count;
        var starts = new List<(int FirstFrame, int Keyframe)>();
        for (int i = 0; i < packets.Count; i++)
        {
            if (packets.Flags[i] != PacketFlags.Keyframe)
                continue;
            int frame = shown.BinarySearch(packets.Pts[i]);
            if (frame > 0 && frame + 1 < frames)
                starts.Add((frame + 1, i));
        }

        starts.Sort();
        return (starts, frames);
    }

    /// <summary>
    /// Where a stopped run goes on from (backlog "Resume after Stop, across visits"): <paramref name="scored"/>'s ranges
    /// from the start (<see cref="FromStart"/>) reach some frame; the rest is scored from the last place a range can start
    /// at or before their last frame (decoded from the keyframe before it, so at most a keyframe interval is scored
    /// again: the frames both have, which ShotDetector compares), to the video's end. The earlier ranges it needs are
    /// those starting before it (a range starting there or later is scored again whole). None reached, or none to
    /// start from but frame 0: the run starts over (no earlier ranges, the range from frame 0). Every frame reached: no
    /// range, the earlier ranges are the whole video's.
    /// </summary>
    public Continuation Continue(IEnumerable<RangeScores> scored)
    {
        var earlier = FromStart(scored).ToList();
        var (starts, frames) = RangeStarts();
        if (earlier.Count == 0)
            return new Continuation([], new FrameRange(0, frames, 0));
        long reached = earlier[^1].FirstFrame + earlier[^1].FrameCount;
        if (reached >= frames)
            return new Continuation(earlier, null);
        var start = starts.LastOrDefault(s => s.FirstFrame <= reached - 1);
        if (start.FirstFrame == 0)
            return new Continuation([], new FrameRange(0, frames, 0));
        earlier.RemoveAll(r => r.FirstFrame >= start.FirstFrame);
        return new Continuation(earlier, new FrameRange(start.FirstFrame, frames, start.Keyframe));
    }

    /// <summary>
    /// The frames ShotDetector's <see cref="ShotDetection.ScoreRangeAsync"/> takes for <paramref name="range"/>: decoded
    /// from its keyframe (the packets before it neither decoded nor read), the frame before its first, its own, and the
    /// next range's first (the overlap frame), or to the video's end for the last range; paused as
    /// <see cref="FramesAsync"/> is; each frame given measured by <paramref name="picture"/>, if any (a meter of the
    /// range, which measures only its own frames).
    /// </summary>
    public IAsyncEnumerable<YuvFrame> RangeFramesAsync(FrameRange range, int every, Func<int, ValueTask<bool>> between,
        CancellationToken cancellationToken = default, PictureMeter? picture = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        int from = Math.Max(range.FirstFrame - 1, 0);
        int limit = range.EndFrame >= ShownFrames ? int.MaxValue : range.EndFrame - from + 1;
        return Paused(Measured(Frames(range.Keyframe), picture), limit, every, between, cancellationToken);
    }

    /// <summary>
    /// Scores <paramref name="range"/> (<see cref="ShotDetection.ScoreRangeAsync"/> on <see cref="RangeFramesAsync"/>),
    /// with ShotDetector.FastYuv's converter unless the options name another. Frames that end early (<paramref name="between"/>
    /// false) end the range there. <paramref name="picture"/>, a meter of the range, measures its frames as they are read.
    /// </summary>
    public Task<RangeScores> ScoreRangeAsync(FrameRange range, int every, Func<int, ValueTask<bool>> between,
        DetectionOptions? options = null, CancellationToken cancellationToken = default, PictureMeter? picture = null) =>
        ShotDetection.ScoreRangeAsync(Video, RangeFramesAsync(range, every, between, cancellationToken, picture),
            range.FirstFrame, WithConverter(options), cancellationToken);

    /// <summary>
    /// Scores <paramref name="range"/> as <see cref="ScoreRangeAsync(FrameRange, int, Func{int, ValueTask{bool}}, DetectionOptions?, CancellationToken, PictureMeter?)"/>
    /// does, continuing from <paramref name="earlier"/> (<see cref="Continue"/>'s: a stopped run resumed), so that the
    /// options' <see cref="DetectionOptions.OnShot"/> is told the rest of the whole video's shots as they are known
    /// (ShotDetector's <see cref="ShotDetection.ScoreRangeAsync(YuvVideo, IEnumerable{RangeScores}, IAsyncEnumerable{YuvFrame}, long, DetectionOptions?, CancellationToken)"/>).
    /// </summary>
    public Task<RangeScores> ScoreRangeAsync(IReadOnlyList<RangeScores> earlier, FrameRange range, int every,
        Func<int, ValueTask<bool>> between, DetectionOptions? options = null, CancellationToken cancellationToken = default,
        PictureMeter? picture = null) =>
        earlier.Count == 0
            ? ScoreRangeAsync(range, every, between, options, cancellationToken, picture)
            : ShotDetection.ScoreRangeAsync(Video, earlier, RangeFramesAsync(range, every, between, cancellationToken, picture),
                range.FirstFrame, WithConverter(options), cancellationToken);

    /// <summary>
    /// Merges ranges' scores (<see cref="ShotDetection.Merge"/>) with the options they were scored with: the whole
    /// video's result, byte for byte as <see cref="Detect"/>'s, when they cover it; for ranges that follow each other
    /// from the start but stop early, the result for the frames they cover, as a run stopped there.
    /// </summary>
    /// <exception cref="ShotDetectionException">The ranges don't make up the video's frames (ShotDetector's reasons).</exception>
    public DetectionResult Merge(IEnumerable<RangeScores> ranges, DetectionOptions? options = null) =>
        ShotDetection.Merge(Video, ranges, WithConverter(options));

    /// <summary>
    /// The ranges from the first on that <see cref="Merge"/> takes: each next one while the one before reaches into it
    /// (has its first frame, the overlap); the rest (after a gap: a range stopped early, or one missing) left out.
    /// </summary>
    public static IReadOnlyList<RangeScores> FromStart(IEnumerable<RangeScores> ranges)
    {
        var taken = new List<RangeScores>();
        foreach (var range in ranges.OrderBy(r => r.FirstFrame))
        {
            long reached = taken.Count == 0 ? 0 : taken[^1].FirstFrame + taken[^1].FrameCount - 1;
            if (range.FrameCount == 0 || range.FirstFrame > reached)
                break;
            taken.Add(range);
        }

        return taken;
    }

    /// <summary>
    /// The options with ShotDetector.FastYuv's converter unless they name another, and its <see cref="Yadif"/> unless they
    /// name another deinterlacer (frames from the caller have no FFmpeg filter: used only when the run deinterlaces).
    /// </summary>
    static DetectionOptions WithConverter(DetectionOptions? options)
    {
        options ??= new DetectionOptions();
        if (options.Yuv420Converter is null)
            options = options with { Yuv420Converter = new SwscaleYuv420() };
        return options.Yadif is null ? options with { Yadif = new Yadif() } : options;
    }

    /// <summary>
    /// Whether a run with <paramref name="options"/> deinterlaces the frames, as ShotDetector decides it: always with
    /// <see cref="DeinterlaceMode.On"/>, with <see cref="DeinterlaceMode.Auto"/> when the field order is interlaced (not
    /// progressive or unknown). Such a run can't be scored in ranges yet (ShotDetector's <c>ScoreRange</c> refuses it:
    /// yadif reads the frames on either side of each, shotdetector#628), so it is detected whole, on one worker.
    /// </summary>
    public bool Deinterlaces(DetectionOptions? options) => options?.Deinterlace == DeinterlaceMode.On ||
        (options?.Deinterlace == DeinterlaceMode.Auto && Video.FieldOrder is not (FrameReader.FieldOrder.Progressive or FrameReader.FieldOrder.Unknown));
}
