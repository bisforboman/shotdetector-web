using System.Text.Json.Nodes;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A run's shots in editors' timeline formats, as scenedetect's save-edl, save-fcp (fcpx and fcp7), save-otio, save-qp
/// and save-keyframes write them, and Subtitle Edit's shot changes (<c>shotdetect --save-shotchanges</c>): ShotDetector's
/// <see cref="Timeline"/> with its defaults; and YouTube's chapters (<see cref="Chapters"/>), Timeline's too. The page's
/// downloads (the App's <c>TimelineFile</c> and <c>YouTubeChapters</c>) and the files NativeCheck writes for
/// app-check.mjs and the page check to compare with.
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
        ("keyframes", "keyframes.txt"),
        ("shotchanges", "shotchanges"),
    ];

    /// <summary>YouTube's shortest chapter, in seconds (<see cref="Timeline.YouTubeChapters"/>' least minimum).</summary>
    public const int YouTubeMinimum = 10;

    /// <summary>The page's shortest chapter to begin with, in seconds (the owner's choice, 2026-10-31).</summary>
    public const int DefaultMinimum = 30;

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
            "keyframes" => Timeline.Keyframes(named),
            "shotchanges" => Timeline.ShotChanges(named),
            _ => throw new ArgumentException($"No timeline format '{format}'.", nameof(format)),
        };
    }

    /// <summary>
    /// The result's YouTube chapters of at least <paramref name="minimum"/> seconds as the App's JSON (camelCase), which
    /// NativeCheck also writes: <c>{ minimum, starts, text, notMade }</c>. <c>starts</c> each chapter's start in whole
    /// seconds (<see cref="Timeline.ChapterStarts(DetectionResult, double)"/>), <c>text</c> the description's lines
    /// (<see cref="Timeline.YouTubeChapters"/>: "0:00 Chapter 1", a title each for the user to rename) or null,
    /// <c>notMade</c> why there are none (Timeline's words: fewer than three chapters) or null. Timeline's rule; the
    /// page owns only what is around it (shotdetector#648).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A minimum under YouTube's 10 seconds.</exception>
    public static string Chapters(DetectionResult result, int minimum)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, YouTubeMinimum);
        string? text = null, notMade = null;
        try
        {
            text = Timeline.YouTubeChapters(result, minimum);
        }
        catch (InvalidOperationException e)
        {
            notMade = e.Message;
        }

        var starts = new JsonArray();
        if (text is not null)
        {
            // Whole seconds (Timeline's TimeSpans are whole seconds from the video's start).
            foreach (var start in Timeline.ChapterStarts(result, minimum))
                starts.Add((JsonNode)(long)start.TotalSeconds);
        }

        return new JsonObject
        {
            ["minimum"] = minimum,
            ["starts"] = starts,
            ["text"] = text,
            ["notMade"] = notMade,
        }.ToJsonString();
    }
}
