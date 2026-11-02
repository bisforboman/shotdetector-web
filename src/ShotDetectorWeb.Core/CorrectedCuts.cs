using System.Globalization;
using System.Text;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A run's cuts as a viewer corrected them on the page (issue #90, decision 2026-11-02-correcting-cuts.md), made into a
/// result by ShotDetector's <see cref="ShotDetection.LoadScenes(YuvVideo, TextReader, string, DetectionOptions)"/>.
/// The page doesn't make that result itself. The corrected shots' first frames are written as a one-column scene list
/// (<see cref="SceneList"/>). NativeCheck gives the same text to <c>LoadScenes(path)</c> on the FFmpeg path and
/// compares every download made from the two results.
/// </summary>
public static class CorrectedCuts
{
    /// <summary>The scene list's one column: each shot's first frame, 1-based, as scenedetect's list-scenes numbers it.</summary>
    public const string Column = "Start Frame";

    /// <summary>
    /// The scene list CSV for shots that start at <paramref name="starts"/> (frames counted from 0, as decoded): the
    /// header, then a 1-based frame a row, sorted, each once.
    /// </summary>
    public static string SceneList(IEnumerable<int> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var csv = new StringBuilder(Column).Append('\n');
        foreach (int start in starts.Distinct().Order())
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start, nameof(starts));
            csv.Append((start + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return csv.ToString();
    }

    /// <summary>
    /// The options <c>LoadScenes</c> is given for a run made with <paramref name="run"/>: the run's own, without its
    /// progress and shot callbacks, and ending at <paramref name="endFrame"/> (exclusive, counted from 0) for a run
    /// stopped there. Null means the whole video.
    /// </summary>
    public static DetectionOptions Options(DetectionOptions run, int? endFrame)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run with
        {
            Progress = null,
            OnShot = null,
            EndTime = endFrame?.ToString(CultureInfo.InvariantCulture) ?? run.EndTime,
        };
    }

    /// <summary>
    /// The result for shots that start at <paramref name="starts"/> in <paramref name="video"/>:
    /// <c>LoadScenes(video, SceneList(starts), Column, Options(run, endFrame))</c>. It has no stats (nothing is
    /// decoded), so a run's stats CSV stays the run's.
    /// </summary>
    public static DetectionResult Load(YuvVideo video, IEnumerable<int> starts, DetectionOptions run, int? endFrame)
    {
        ArgumentNullException.ThrowIfNull(video);
        using var list = new StringReader(SceneList(starts));
        return ShotDetection.LoadScenes(video, list, Column, Options(run, endFrame));
    }
}
