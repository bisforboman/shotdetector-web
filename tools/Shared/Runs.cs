using ShotDetector;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The detector runs the native check makes on every sample, and the files their results are written to. The wasm check
/// (tools/app-check.mjs) holds the same runs as the App's options JSON: change both together.
/// </summary>
internal static class Runs
{
    /// <summary>Each of the five detectors with scenedetect's defaults, and all five in one run, every one with stats.</summary>
    public static readonly (string Name, DetectionOptions Options)[] All =
    [
        ("adaptive", new DetectionOptions { Detector = DetectorKind.Adaptive, CollectStats = true }),
        ("content", new DetectionOptions { Detector = DetectorKind.Content, CollectStats = true }),
        ("threshold", new DetectionOptions { Detector = DetectorKind.Threshold, CollectStats = true }),
        ("histogram", new DetectionOptions { Detector = DetectorKind.Histogram, CollectStats = true }),
        ("hash", new DetectionOptions { Detector = DetectorKind.Hash, CollectStats = true }),
        ("all", new DetectionOptions
        {
            Detectors = [new(DetectorKind.Adaptive), new(DetectorKind.Content), new(DetectorKind.Threshold), new(DetectorKind.Histogram), new(DetectorKind.Hash)],
            CollectStats = true,
        }),
    ];

    /// <summary>The scene list CSV (as scenedetect list-scenes writes it) and the stats CSV (scenedetect -s).</summary>
    public static (string Scenes, string Stats) Csvs(DetectionResult result) =>
        (Shots.Csv(result.Shots, cuts: result.Cuts), result.Stats?.Csv(result.Video.Position) ?? "");

    /// <summary>Where a sample's run goes: {sample}.{run}.scenes.csv and .stats.csv.</summary>
    public static string BaseName(string sample, string run) => $"{Path.GetFileNameWithoutExtension(sample)}.{run}";
}
