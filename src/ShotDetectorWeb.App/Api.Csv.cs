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
    public static string ScenesCsv() => Shots.Csv(Result.Shots, cuts: Result.Cuts);

    /// <summary>The last <see cref="Detect"/>'s stats CSV (scenedetect -s's), a string as it is (<see cref="ScenesCsv"/>).</summary>
    [JSExport]
    public static string StatsCsv() => Result.Stats!.Csv(Result.Video.Position);
}
