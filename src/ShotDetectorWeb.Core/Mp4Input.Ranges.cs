using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// A range of the video's frames, scored on a runtime of its own (a browser Web Worker) by
/// <see cref="Mp4Input.ScoreRangeAsync"/>, its scores merged with the others' by <see cref="Mp4Input.Merge"/>
/// (docs/decisions/2026-10-26-workers.md). <see cref="FirstFrame"/> is ShotDetector's <c>firstFrame</c>: 0 for the first
/// range, else one past a keyframe, which the range decodes from and gives as the frame before its first.
/// </summary>
/// <param name="FirstFrame">The range's first frame (0-based, display order).</param>
/// <param name="EndFrame">The frame after its last: the next range's first, or the video's frame count.</param>
/// <param name="Keyframe">The video packet (decode order, an index of <see cref="Mp4Input.Packets"/>) it decodes from.</param>
public sealed record FrameRange(int FirstFrame, int EndFrame, int Keyframe);

public sealed partial class Mp4Input
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
    /// worker told the count and its index decodes the range the page planned.
    /// </summary>
    public IReadOnlyList<FrameRange> Ranges(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
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

        // The places a range can start: one past each keyframe but the first frame's, before the last frame.
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
        var ranges = new List<FrameRange>();
        (int FirstFrame, int Keyframe) last = (0, 0);
        for (int r = 1; r < count && starts.Count > 0; r++)
        {
            long target = (long)frames * r / count;
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
    /// The frames ShotDetector's <see cref="ShotDetection.ScoreRangeAsync"/> takes for <paramref name="range"/>: decoded
    /// from its keyframe (the packets before it neither decoded nor read), the frame before its first, its own, and the
    /// next range's first (the overlap frame), or to the video's end for the last range; paused as
    /// <see cref="FramesAsync"/> is.
    /// </summary>
    public IAsyncEnumerable<YuvFrame> RangeFramesAsync(FrameRange range, int every, Func<int, ValueTask<bool>> between,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        int from = Math.Max(range.FirstFrame - 1, 0);
        int limit = range.EndFrame >= ShownFrames ? int.MaxValue : range.EndFrame - from + 1;
        return Paused(Frames(range.Keyframe), limit, every, between, cancellationToken);
    }

    /// <summary>
    /// Scores <paramref name="range"/> (<see cref="ShotDetection.ScoreRangeAsync"/> on <see cref="RangeFramesAsync"/>),
    /// with ShotDetector.FastYuv's converter unless the options name another. Frames that end early (<paramref name="between"/>
    /// false) end the range there.
    /// </summary>
    public Task<RangeScores> ScoreRangeAsync(FrameRange range, int every, Func<int, ValueTask<bool>> between,
        DetectionOptions? options = null, CancellationToken cancellationToken = default) =>
        ShotDetection.ScoreRangeAsync(Video, RangeFramesAsync(range, every, between, cancellationToken), range.FirstFrame,
            WithConverter(options), cancellationToken);

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

    static DetectionOptions WithConverter(DetectionOptions? options)
    {
        options ??= new DetectionOptions();
        return options.Yuv420Converter is null ? options with { Yuv420Converter = new SwscaleYuv420() } : options;
    }
}
