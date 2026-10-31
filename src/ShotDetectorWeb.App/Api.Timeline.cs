using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// The run card's downloads for editors: the last run's shots in scenedetect's timeline formats (ShotDetector's
/// <c>Timeline</c> through <see cref="TimelineFiles"/>).
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The last <see cref="Detect"/>'s shots in <paramref name="format"/> (<c>edl</c>, <c>fcpx</c>, <c>fcp7</c>,
    /// <c>otio</c> or <c>qp</c>) for the file named <paramref name="fileName"/>, the picked file's name: the XMLs and the
    /// OTIO name the file at the root (<see cref="TimelineFiles"/>), as the browser doesn't know its folder.
    /// </summary>
    [JSExport]
    public static string TimelineFile(string format, string fileName) => TimelineFiles.Write(Result, format, fileName);
}
