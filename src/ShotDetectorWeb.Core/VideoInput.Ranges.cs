using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// A range of the video's frames, scored on a runtime of its own (a browser Web Worker) by
/// <see cref="VideoInput.ScoreRangeAsync"/>, its scores merged with the others' by <see cref="VideoInput.Merge"/>
/// (docs/decisions/2026-10-26-workers.md). <see cref="FirstFrame"/> is ShotDetector's <c>firstFrame</c>: 0 for the first
/// range, else as far past a keyframe as ShotDetector's margin before a range
/// (<see cref="ShotDetection.RangeFrames"/>: one frame, two when the run deinterlaces), so that the range decodes from
/// the keyframe and gives it as its first frame, <see cref="From"/>.
/// </summary>
/// <param name="FirstFrame">The range's first frame (0-based, display order).</param>
/// <param name="EndFrame">The frame after its last: the next range's first, or the video's frame count.</param>
/// <param name="Keyframe">The video packet (decode order, an index of <see cref="VideoInput.Packets"/>) it decodes from.</param>
/// <param name="From">The first frame it gives ShotDetector, the keyframe's (<see cref="ShotDetection.RangeFrames"/>' <c>From</c>).</param>
/// <param name="To">One past the last frame it gives (<see cref="ShotDetection.RangeFrames"/>' <c>To</c>), unless it is the
/// last range, which gives the frames to the video's end.</param>
public sealed record FrameRange(int FirstFrame, int EndFrame, int Keyframe, int From, long To);

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
    /// Splits the video into at most <paramref name="count"/> ranges about equal in frames for a run with
    /// <paramref name="options"/>, each after the first starting ShotDetector's margin past a keyframe
    /// (<see cref="ShotDetection.RangeFrames"/>: the frames before it the range gives), the keyframe nearest to an equal
    /// split's start; fewer when the keyframes are too few (one range for a video with one keyframe). The same file and
    /// options give the same ranges on every runtime, so a worker told the count and its index decodes the range the page
    /// planned. With <paramref name="from"/> (a run resumed: <see cref="Continue"/>'s range's first frame), the frames from
    /// there on are split the same way, the first range starting there.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="from"/> is neither 0 nor where a range can start.</exception>
    public IReadOnlyList<FrameRange> Ranges(DetectionOptions? options, int count, int from = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var (starts, frames) = RangeStarts(options);
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
            ranges.Add(Range(options, last.FirstFrame, nearest.FirstFrame, last.Keyframe));
            last = nearest;
        }

        ranges.Add(Range(options, last.FirstFrame, frames, last.Keyframe));
        return ranges;
    }

    /// <summary>The range [<paramref name="firstFrame"/>, <paramref name="endFrame"/>) decoded from <paramref name="keyframe"/>, with the frames ShotDetector takes for it.</summary>
    FrameRange Range(DetectionOptions? options, int firstFrame, int endFrame, int keyframe)
    {
        var (from, to) = ShotDetection.RangeFrames(Video, options ?? new DetectionOptions(), firstFrame, endFrame);
        return new FrameRange(firstFrame, endFrame, keyframe, (int)from, to);
    }

    /// <summary>
    /// The places a range can start for a run with <paramref name="options"/>, in frame order: the first frame past each
    /// keyframe but the first frame's whose range starts its frames there (<see cref="ShotDetection.RangeFrames"/>'
    /// <c>From</c>), before the last frame, each with its keyframe's packet (decode order); and the frames the video shows.
    /// </summary>
    (List<(int FirstFrame, int Keyframe)> Starts, int Frames) RangeStarts(DetectionOptions? options)
    {
        options ??= new DetectionOptions();
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
            if (frame <= 0)
                continue;
            int first = frame + 1;
            while (first < frames && ShotDetection.RangeFrames(Video, options, first, frames).From < frame)
                first++;
            if (first < frames)
                starts.Add((first, i));
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
    public Continuation Continue(IEnumerable<RangeScores> scored, DetectionOptions? options)
    {
        var earlier = FromStart(scored, options).ToList();
        var (starts, frames) = RangeStarts(options);
        if (earlier.Count == 0)
            return new Continuation([], Range(options, 0, frames, 0));
        // A range's last scores read frames past them (Lookahead), but for the video's last frames, which have none.
        long reached = earlier[^1].FirstFrame + earlier[^1].FrameCount;
        if (reached >= frames)
            return new Continuation(earlier, null);
        reached -= Lookahead(options);
        var start = starts.LastOrDefault(s => s.FirstFrame <= reached - 1);
        if (start.FirstFrame == 0)
            return new Continuation([], Range(options, 0, frames, 0));
        earlier.RemoveAll(r => r.FirstFrame >= start.FirstFrame);
        return new Continuation(earlier, Range(options, start.FirstFrame, frames, start.Keyframe));
    }

    /// <summary>
    /// The frames ShotDetector's <see cref="ShotDetection.ScoreRangeAsync"/> takes for <paramref name="range"/>: decoded
    /// from its keyframe (the packets before it neither decoded nor read), ShotDetector's frames for it
    /// (<see cref="ShotDetection.RangeFrames"/>: the frames before its first that its scores read, its own, and the next
    /// range's first and any after it that they read), or to the video's end for the last range; paused as
    /// <see cref="FramesAsync"/> is; each frame given measured by <paramref name="picture"/>, if any (a meter of the
    /// range, which measures only its own frames).
    /// </summary>
    public IAsyncEnumerable<YuvFrame> RangeFramesAsync(FrameRange range, int every, Func<int, ValueTask<bool>> between,
        CancellationToken cancellationToken = default, PictureMeter? picture = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        int limit = range.EndFrame >= ShownFrames ? int.MaxValue : (int)(range.To - range.From);
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
    /// The ranges from the first on that <see cref="Merge"/> takes for a run with <paramref name="options"/>: each next
    /// one while the one before reaches into it (scored its first frame, the overlap); the rest (after a gap: a range
    /// stopped early, or one missing) left out.
    /// </summary>
    public IReadOnlyList<RangeScores> FromStart(IEnumerable<RangeScores> ranges, DetectionOptions? options)
    {
        int lookahead = Lookahead(options);
        var taken = new List<RangeScores>();
        foreach (var range in ranges.OrderBy(r => r.FirstFrame))
        {
            long reached = taken.Count == 0 ? 0 : taken[^1].FirstFrame + taken[^1].FrameCount - 1 - lookahead;
            if (range.FrameCount == 0 || range.FirstFrame > reached)
                break;
            taken.Add(range);
        }

        return taken;
    }

    /// <summary>
    /// The frames a range's scores read past their last (<see cref="ShotDetection.RangeFrames"/>' <c>To</c> past the
    /// next range's first frame, its overlap: none, or yadif's next frame when the run deinterlaces): a range's last
    /// scores that many frames before the last it was given, as the merge counts them.
    /// </summary>
    int Lookahead(DetectionOptions? options) =>
        (int)(ShotDetection.RangeFrames(Video, options ?? new DetectionOptions(), 0, 1).To - 2);

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
}
