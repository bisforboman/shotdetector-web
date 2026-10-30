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

    /// <summary>
    /// The last <see cref="Detect"/>'s stats CSV (scenedetect -s's) as a <c>Blob</c> (text/csv, UTF-8): written a row at
    /// a time (<see cref="Stats.WriteCsv(TextWriter, Func{int, FrameTime})"/>) and copied out in parts as it is
    /// written (<see cref="BlobWriter"/>), so the whole CSV, 20 million characters on a two-hour film, is never one
    /// string in the heap, which never shrinks (README "What the heap holds").
    /// </summary>
    [JSExport]
    public static JSObject StatsCsv() =>
        BlobWriter.Write(writer => Result.Stats!.WriteCsv(writer, Result.Video.Position), "text/csv");
}
