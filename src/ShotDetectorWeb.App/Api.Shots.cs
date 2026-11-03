using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// The downloads beside the CSVs: the last run's shots (its corrected ones once <see cref="Correct"/> has them) as
/// ShotDetector writes them as JSON and as HTML.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>The last <see cref="Detect"/>'s shots as <see cref="Shots.Json"/> writes them (indented JSON).</summary>
    [JSExport]
    public static string ShotsJson() => Shots.Json(Shown.Shots);

    /// <summary>
    /// The last <see cref="Detect"/>'s shots as scenedetect's save-html page (<see cref="Shots.Html"/>), the cut list
    /// the run's cuts, without images: the page's stills are its own decode, not files beside the download.
    /// </summary>
    [JSExport]
    public static string ShotsHtml() => Shots.Html(Shown.Shots, cuts: Shown.Cuts);
}
