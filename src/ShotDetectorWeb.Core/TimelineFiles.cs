using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A run's shots in editors' timeline formats, as scenedetect's save-edl, save-fcp (fcpx and fcp7), save-otio and
/// save-qp write them: ShotDetector's <see cref="Timeline"/> with its defaults. The page's downloads (the App's
/// <c>TimelineFile</c>) and the files NativeCheck writes for app-check.mjs and the page check to compare with.
/// </summary>
/// <remarks>
/// The frame-source path's result has no <see cref="DetectionResult.VideoPath"/>, and a browser never learns the folder
/// of a picked file, so the exports are given the file's name at the root, <c>/{name}</c>: the EDL's title, the XMLs'
/// and the OTIO's names are the file's, and their media paths (<c>file:///{name}</c>, OTIO's <c>/{name}</c>) are what
/// an editor relinks to the file where it really is. The same string on the FFmpeg path's result gives the same
/// bytes, which is what NativeCheck compares.
/// </remarks>
public static class TimelineFiles
{
    /// <summary>The formats, each with the extension of its file: <c>{name}.{run}.{extension}</c>, <c>{name}.{extension}</c> on the page.</summary>
    public static readonly IReadOnlyList<(string Format, string Extension)> Formats =
    [
        ("edl", "edl"),
        ("fcpx", "fcpxml"),
        ("fcp7", "fcp7.xml"),
        ("otio", "otio"),
        ("qp", "qp"),
    ];

    /// <summary>The result's shots in <paramref name="format"/> (one of <see cref="Formats"/>), for the file named <paramref name="fileName"/>.</summary>
    /// <exception cref="ArgumentException">An unknown format, or a file name with a folder in it.</exception>
    /// <exception cref="InvalidOperationException">FCPXML and FCP7 of a result without shots (ShotDetector's "No shots to export.").</exception>
    public static string Write(DetectionResult result, string format, string fileName)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrEmpty(fileName) || fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException($"'{fileName}' isn't a file's name.", nameof(fileName));
        var named = result with { VideoPath = "/" + fileName };
        return format switch
        {
            "edl" => Timeline.Edl(named),
            "fcpx" => Timeline.Fcpx(named),
            "fcp7" => Timeline.Fcp7(named),
            "otio" => Timeline.Otio(named),
            "qp" => Timeline.Qp(named),
            _ => throw new ArgumentException($"No timeline format '{format}'.", nameof(format)),
        };
    }
}
