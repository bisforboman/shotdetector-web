// YouTube chapters (TimelineFiles.Chapters: ShotDetector's Timeline.YouTubeChapters and ChapterStarts, the run card's
// chapter list): every run's chapters (Program.cs writes them, and compares them with the FFmpeg path's) against
// YouTube's rules and the cuts they start at. Timeline's rule is ShotDetector's to test; this checks what the page gets.
using System.Text.Json.Nodes;
using ShotDetector;
using ShotDetectorWeb;

namespace ShotDetectorWeb.Checks;

internal static class ChaptersCheck
{
    /// <summary>The minimums every run's chapters are written at: YouTube's own, and the page's default.</summary>
    public static readonly int[] Minimums = [TimelineFiles.YouTubeMinimum, TimelineFiles.DefaultMinimum];

    /// <summary>
    /// A result's chapters (TimelineFiles.Chapters' JSON) against YouTube's rules (the first at 0:00, at least three,
    /// ascending, each at least the minimum, the last to the end), the cuts (each start a cut's time rounded up to the
    /// second) and their text (a line a chapter, its start as YouTube shows it: m:ss, h:mm:ss from an hour on); a list, or a reason.
    /// </summary>
    public static List<string> Problems(DetectionResult result, string json)
    {
        var p = new List<string>();
        var c = JsonNode.Parse(json)!.AsObject();
        int minimum = (int)c["minimum"]!;
        string? text = (string?)c["text"], notMade = (string?)c["notMade"];
        var s = c["starts"]!.AsArray().Select(n => (long)n!).ToList();
        if ((text is null) == (notMade is null))
            p.Add("a list and a reason, or neither");
        if (text is null)
        {
            if (s.Count != 0)
                p.Add("starts without a list");
            return p;
        }

        var shots = result.Shots;
        double origin = shots[0].Start.Seconds, end = shots[^1].End.Seconds - origin;
        var cuts = shots.Skip(1).Select(shot => (long)Math.Ceiling(Math.Round((shot.Start.Seconds - origin) * 1000) / 1000)).ToHashSet();
        if (s.Count < 3)
            p.Add($"{s.Count} chapters");
        if (s.Count > 0 && s[0] != 0)
            p.Add("the first not at 0:00");
        for (int i = 1; i < s.Count; i++)
        {
            if (s[i] - s[i - 1] < Math.Max(minimum, TimelineFiles.YouTubeMinimum))
                p.Add($"chapter {i} {s[i] - s[i - 1]} s");
            if (!cuts.Contains(s[i]))
                p.Add($"chapter {i + 1} at {s[i]} s, no cut's second");
        }

        if (s.Count > 0 && end - s[^1] < minimum)
            p.Add($"the last chapter {end - s[^1]:0.###} s to the end");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != s.Count || lines.Where((line, i) => !line.StartsWith(Stamp(s[i]) + " ", StringComparison.Ordinal)).Any())
            p.Add("the lines aren't a chapter each at its start");
        return p;
    }

    /// <summary>A chapter's start as YouTube shows it: "m:ss", or "h:mm:ss" from an hour on.</summary>
    static string Stamp(long seconds) => seconds >= 3600
        ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
        : $"{seconds / 60}:{seconds % 60:00}";
}
