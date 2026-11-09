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
    /// from 0 as decoded, frame 0 among them: the run's <c>cutFrames</c> and frame 0 give its own shots), or
    /// <c>null</c> to go back to the run's own. Returns the result as <see cref="Detect"/>'s JSON (the shots and their
    /// cuts' frames, the run's frame count, rate, duration, whether it was stopped, and its warnings). The corrected
    /// result keeps the run's frame times and length (<see cref="CorrectedCuts"/>): a stopped run's ends where it stopped.
    /// </summary>
    [JSExport]
    public static string Correct(string startsJson)
    {
        var run = Result;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var starts = JsonSerializer.Deserialize(startsJson, CorrectionsJson.Default.Int32Array);
        // The frames the run read from 0 (Answered's): a run leaving the reference tone out has its shots between the
        // tones, and LoadScenes leaves a cut outside them out.
        int read = _read;
        bool stopped = read < input.ShownFrames;
        if (starts is null)
        {
            _corrected = null;
        }
        else
        {
            if (starts.Any(s => s < 0 || s >= read))
                throw new ArgumentOutOfRangeException(nameof(startsJson), $"A shot starts outside the {read} frames read.");
            if (!starts.Contains(0))
                throw new ArgumentException("The first shot starts at frame 0.", nameof(startsJson));
            _corrected = CorrectedCuts.Load(run, starts, _keptOptions ?? new DetectionOptions());
        }

        var shown = Shown;
        using var shots = JsonDocument.Parse(Shots.Json(shown.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(shots.RootElement, shown.CutFrames, read, run.Video.FrameRate, run.Video.Duration,
                stopped, _warnings),
            ApiJson.Default.DetectResponse);
    }
}

[JsonSerializable(typeof(int[]))]
partial class CorrectionsJson : JsonSerializerContext;
