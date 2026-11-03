using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A run's cuts as a viewer corrected them on the page (issue #90, decision 2026-11-02-correcting-cuts.md), made into a
/// result by ShotDetector's <see cref="ShotDetection.LoadScenes(DetectionResult, IEnumerable{int}, DetectionOptions)"/>
/// (3.28.0, shotdetector#707): the run's own frame times and length with the corrected cuts, so moving one cut moves no
/// other, at a variable frame rate, after an edit list that ends early or on a fragmented file too. The cuts are frames
/// counted from the video's first frame as decoded (<see cref="DetectionResult.CutFrames"/> gives the run's), not the
/// scene list's Start Frame − 1, which is a time × the average rate. NativeCheck gives the same cuts to the same
/// <c>LoadScenes</c> of the FFmpeg path's run and compares every download made from the two results.
/// </summary>
public static class CorrectedCuts
{
    /// <summary>
    /// The options <c>LoadScenes</c> is given for a run made with <paramref name="run"/>: the run's own (its
    /// MinSceneLength, DropShortScenes and MergeLastScene), without its progress and shot callbacks or a time range,
    /// which is the run's (a stopped run's ends where it stopped).
    /// </summary>
    public static DetectionOptions Options(DetectionOptions run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run with { Progress = null, OnShot = null, StartTime = null, EndTime = null, RangeDuration = null,
            Start = null, End = null, Duration = null };
    }

    /// <summary>
    /// The result of <paramref name="run"/> (made with <paramref name="options"/>) with its shots starting at
    /// <paramref name="starts"/> (frames counted from 0 as decoded, frame 0 among them or not): its stats, spans,
    /// warnings and video are the run's. The run's own starts (<see cref="DetectionResult.CutFrames"/>) give the run's
    /// result back.
    /// </summary>
    public static DetectionResult Load(DetectionResult run, IEnumerable<int> starts, DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(starts);
        return ShotDetection.LoadScenes(run, starts.Where(s => s > 0), Options(options));
    }
}
