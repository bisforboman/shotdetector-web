using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>The run card's YouTube chapters: the last run's cuts as a video description's chapter list, ShotDetector's <c>Timeline</c>'s.</summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The last <see cref="Detect"/>'s chapters of at least <paramref name="minimum"/> seconds
    /// (<see cref="TimelineFiles.Chapters"/>) as JSON: <c>{ minimum, starts, text, notMade }</c>, <c>text</c> the lines
    /// for the description or null, <c>notMade</c> why there are none or null.
    /// </summary>
    [JSExport]
    public static string YouTubeChapters(int minimum) => TimelineFiles.Chapters(Result, minimum);
}
