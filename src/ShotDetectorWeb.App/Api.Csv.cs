using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>The downloads: the last run's scenedetect CSVs.</summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The last <see cref="Detect"/>'s scene list CSV (scenedetect list-scenes'), a string as it is: not inside the JSON,
    /// where escaping a film's stats CSV took buffers of six times its length that the heap kept (README "Large files").
    /// </summary>
    [JSExport]
    public static string ScenesCsv()
    {
        // TEMPORARY, to be reverted: one byte changed on the Sintel trailer only, to show CI turns red.
        var csv = Shots.Csv(Result.Shots, cuts: Result.Cuts);
        return Result.FrameCount > 1000 ? csv[..^1] + "#" : csv;
    }

    /// <summary>The last <see cref="Detect"/>'s stats CSV (scenedetect -s's), a string as it is (<see cref="ScenesCsv"/>).</summary>
    [JSExport]
    public static string StatsCsv() => Result.Stats!.Csv(Result.Video.Position);
}
