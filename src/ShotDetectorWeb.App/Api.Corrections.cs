using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// The last run's cuts as the viewer corrected them (issue #90, decision 2026-11-02-correcting-cuts.md). Once
/// <see cref="Correct"/> has a list, the downloads come from ShotDetector's <c>LoadScenes</c> of it
/// (<see cref="CorrectedCuts"/>): the scenes CSV, the shots as JSON and HTML, the editors' files and the chapters. The
/// stats CSV stays the run's, as its scores don't change.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>The last run's result made from the corrected list, or null when nothing is corrected.</summary>
    static DetectionResult? _corrected;

    /// <summary>
    /// What the downloads are made from: the corrected list's result if there is one, else the run's.
    /// </summary>
    static DetectionResult Shown => _result is null ? Result : _corrected ?? _result;

    /// <summary>
    /// Corrects the last run's cuts: <paramref name="startsJson"/> is the shots' first frames (a JSON array, counted
    /// from 0, frame 0 among them), or <c>null</c> to go back to the run's own. Returns the result as
    /// <see cref="Detect"/>'s JSON (the shots, the run's frame count, rate, duration, whether it was stopped, and its
    /// warnings). For a stopped run, the corrected result also ends where the run stopped.
    /// </summary>
    [JSExport]
    public static string Correct(string startsJson)
    {
        var run = Result;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var starts = JsonSerializer.Deserialize(startsJson, CorrectionsJson.Default.Int32Array);
        bool stopped = run.FrameCount < input.ShownFrames;
        if (starts is null)
        {
            _corrected = null;
        }
        else
        {
            if (starts.Any(s => s < 0 || s >= run.FrameCount))
                throw new ArgumentOutOfRangeException(nameof(startsJson), $"A shot starts outside the {run.FrameCount} frames read.");
            if (!starts.Contains(0))
                throw new ArgumentException("The first shot starts at frame 0.", nameof(startsJson));
            _corrected = CorrectedCuts.Load(input.Video, starts, _keptOptions ?? new DetectionOptions(),
                stopped ? run.FrameCount : null);
        }

        var shown = Shown;
        using var shots = JsonDocument.Parse(Shots.Json(shown.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(shots.RootElement, run.FrameCount, run.Video.FrameRate, run.Video.Duration, stopped,
                run.Warnings),
            ApiJson.Default.DetectResponse);
    }
}

[JsonSerializable(typeof(int[]))]
partial class CorrectionsJson : JsonSerializerContext;
