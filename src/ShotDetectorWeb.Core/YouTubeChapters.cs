using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A run's cuts as YouTube chapters, the lines a video's description lists: the page's own format, not scenedetect's
/// (decision in docs/ideas/editor-exports-and-launch.md; the owner's choices of 2026-10-31: on the page only, at least
/// 30 s by default, editable). YouTube's rules (its help page "Video chapters"): the first timestamp 00:00, at least
/// three, in ascending order, each chapter at least 10 seconds, the last to the video's end.
/// </summary>
/// <remarks>
/// Each chapter starts at a cut, its time rounded down to the whole second (the timestamps are whole seconds), taken
/// in order whenever it is at least the minimum after the chapter before; then the last chapters are dropped while
/// the one left last would be shorter than the minimum to the end. That keeps the most chapters these rules allow,
/// each at a cut. With fewer than three there is no list, and <see cref="Chapters.NotMade"/> says why. Times count
/// from the first shot's start (the player's 0:00). The titles are placeholders ("Chapter 2") for the user to write.
/// </remarks>
public static class YouTubeChapters
{
    /// <summary>YouTube's shortest chapter, in seconds.</summary>
    public const int YouTubeMinimum = 10;

    /// <summary>The page's default shortest chapter, in seconds (the owner's choice).</summary>
    public const int DefaultMinimum = 30;

    /// <summary>The chapters of a run, or why there are none.</summary>
    /// <param name="Minimum">The shortest chapter asked for, in seconds.</param>
    /// <param name="Starts">Each chapter's start in whole seconds, the first 0; empty when <paramref name="NotMade"/>.</param>
    /// <param name="Text">The description's lines, a chapter a line ("01:23 Chapter 2"); null when <paramref name="NotMade"/>.</param>
    /// <param name="NotMade">Why there is no list (fewer than three chapters); null when there is one.</param>
    public sealed record Chapters(int Minimum, IReadOnlyList<int> Starts, string? Text, string? NotMade);

    /// <summary>The result's shots as chapters of at least <paramref name="minimum"/> seconds.</summary>
    public static Chapters From(DetectionResult result, int minimum)
    {
        ArgumentNullException.ThrowIfNull(result);
        var shots = result.Shots;
        if (shots.Count == 0)
            return From([], 0, minimum);
        double origin = shots[0].Start.Seconds;
        return From([.. shots.Skip(1).Select(s => s.Start.Seconds - origin)], shots[^1].End.Seconds - origin, minimum);
    }

    /// <summary>Chapters of at least <paramref name="minimum"/> seconds at <paramref name="cuts"/> (seconds, ascending) in a video <paramref name="end"/> seconds long.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A minimum under YouTube's 10 seconds.</exception>
    public static Chapters From(IReadOnlyList<double> cuts, double end, int minimum)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimum, YouTubeMinimum);
        var starts = new List<int> { 0 };
        foreach (double cut in cuts)
        {
            // Whole seconds, rounded down; the nudge keeps a cut at 24 s that the time base gives as 23.9999999 at 24.
            int second = (int)Math.Floor(cut + 1e-6);
            if (second - starts[^1] >= minimum)
                starts.Add(second);
        }

        while (starts.Count > 1 && end - starts[^1] < minimum)
            starts.RemoveAt(starts.Count - 1);
        if (starts.Count < 3)
        {
            string length = end.ToString("0.#", CultureInfo.InvariantCulture);
            string found = starts.Count == 1 ? "one chapter" : $"{starts.Count} chapters";
            return new Chapters(minimum, [], null,
                $"Only {found} of at least {minimum} s in {length} s at the cuts: YouTube needs at least three.");
        }

        bool hours = end >= 3600;
        var text = new StringBuilder();
        for (int i = 0; i < starts.Count; i++)
            text.Append(CultureInfo.InvariantCulture, $"{Stamp(starts[i], hours)} Chapter {i + 1}\n");
        return new Chapters(minimum, starts, text.ToString(), null);
    }

    /// <summary>The chapters as the App's JSON (camelCase), which NativeCheck also writes.</summary>
    public static string Json(Chapters chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        var starts = new JsonArray();
        foreach (int s in chapters.Starts)
            starts.Add((JsonNode)s);
        return new JsonObject
        {
            ["minimum"] = chapters.Minimum,
            ["starts"] = starts,
            ["text"] = chapters.Text,
            ["notMade"] = chapters.NotMade,
        }.ToJsonString();
    }

    /// <summary>"mm:ss" under an hour ("00:00", "12:05"), "h:mm:ss" in a video an hour or longer ("0:00:00").</summary>
    static string Stamp(int seconds, bool hours) => hours
        ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}");
}
