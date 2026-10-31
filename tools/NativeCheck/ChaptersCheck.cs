// YouTube chapters (the core's YouTubeChapters, the run card's chapter list: the page's own format): hand-made cuts
// against the lines they must give, then every run's chapters (Program.cs writes them) against YouTube's rules.
using ShotDetector;
using ShotDetectorWeb;

namespace ShotDetectorWeb.Checks;

internal static class ChaptersCheck
{
    /// <summary>The minimums every run's chapters are written at: YouTube's own, and the page's default.</summary>
    public static readonly int[] Minimums = [YouTubeChapters.YouTubeMinimum, YouTubeChapters.DefaultMinimum];

    /// <summary>The hand-made cases, each the text it must give (null: none, with a reason). Returns the failures.</summary>
    public static int Run()
    {
        int failures = 0;
        void Case(string name, double[] cuts, double end, int minimum, string? expected)
        {
            var c = YouTubeChapters.From(cuts, end, minimum);
            var problems = Problems(c, cuts, end);
            if (c.Text != expected)
                problems.Add($"gave {Show(c.Text ?? c.NotMade)}, not {Show(expected)}");
            if (problems.Count > 0)
            {
                Console.WriteLine($"Chapters, {name}: {string.Join("; ", problems)}");
                failures++;
            }
        }

        // A cut every 4 s for 2 minutes: chapters at the first cut 30 s or more after the one before.
        double[] every4 = [.. Enumerable.Range(1, 29).Select(i => i * 4.0)];
        Case("a cut every 4 s, 30 s", every4, 120, 30, "00:00 Chapter 1\n00:32 Chapter 2\n01:04 Chapter 3\n");
        Case("a cut every 4 s, 10 s", every4, 120, 10,
            string.Concat(new[] { 0, 12, 24, 36, 48, 60, 72, 84, 96, 108 }.Select((s, i) => $"{s / 60:00}:{s % 60:00} Chapter {i + 1}\n")));
        // Rounded down to the second: 29.96 is 29, too soon after 0; 30.04 is 30.
        Case("rounded down", [29.96, 30.04, 61.5], 100, 30, "00:00 Chapter 1\n00:30 Chapter 2\n01:01 Chapter 3\n");
        // A time base's 23.9999999 for a cut at 24 s is 24.
        Case("a whole second from below", [11.9999999, 23.9999999], 40, 10, "00:00 Chapter 1\n00:12 Chapter 2\n00:24 Chapter 3\n");
        // The last chapter too short to the end is dropped, the one before then the last.
        Case("the last too short", [30, 60, 95], 110, 30, "00:00 Chapter 1\n00:30 Chapter 2\n01:00 Chapter 3\n");
        // Fewer than three: none.
        Case("two chapters", [40], 100, 30, null);
        Case("no cuts", [], 300, 30, null);
        Case("a short video", [3, 6], 9, 10, null);
        // An hour or longer: h:mm:ss for every line.
        Case("an hour", [1200, 2400], 3700, 30, "0:00:00 Chapter 1\n0:20:00 Chapter 2\n0:40:00 Chapter 3\n");
        Case("under an hour, past ten minutes", [700, 1400], 2000, 30, "00:00 Chapter 1\n11:40 Chapter 2\n23:20 Chapter 3\n");
        try
        {
            YouTubeChapters.From([], 100, 9);
            Console.WriteLine("Chapters: a minimum of 9 s taken, under YouTube's 10");
            failures++;
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        Console.WriteLine(failures == 0 ? "Chapters: the hand-made cases agree." : $"Chapters: {failures} hand-made cases failed.");
        return failures;
    }

    /// <summary>A result's chapters against YouTube's rules and the cuts they start at (Program.cs, every run).</summary>
    public static List<string> Problems(DetectionResult result, YouTubeChapters.Chapters c)
    {
        var shots = result.Shots;
        if (shots.Count == 0)
            return c.NotMade is null ? ["chapters of no shots"] : [];
        double origin = shots[0].Start.Seconds;
        return Problems(c, [.. shots.Skip(1).Select(s => s.Start.Seconds - origin)], shots[^1].End.Seconds - origin);
    }

    /// <summary>
    /// YouTube's rules (the first at 00:00, at least three, ascending, each at least the minimum, the last to the end)
    /// and the page's (each a cut rounded down to the second; a list, or a reason, never both; the text the starts).
    /// </summary>
    static List<string> Problems(YouTubeChapters.Chapters c, IReadOnlyList<double> cuts, double end)
    {
        var p = new List<string>();
        if ((c.Text is null) == (c.NotMade is null))
            p.Add("a list and a reason, or neither");
        if (c.Text is null)
        {
            if (c.Starts.Count != 0)
                p.Add("starts without a list");
            return p;
        }

        var s = c.Starts;
        if (s.Count < 3)
            p.Add($"{s.Count} chapters");
        if (s.Count > 0 && s[0] != 0)
            p.Add("the first not at 00:00");
        for (int i = 1; i < s.Count; i++)
        {
            if (s[i] - s[i - 1] < Math.Max(c.Minimum, YouTubeChapters.YouTubeMinimum))
                p.Add($"chapter {i} {s[i] - s[i - 1]} s");
            if (!cuts.Any(cut => (int)Math.Floor(cut + 1e-6) == s[i]))
                p.Add($"chapter {i + 1} at {s[i]} s, no cut's second");
        }

        if (s.Count > 0 && end - s[^1] < c.Minimum)
            p.Add($"the last chapter {end - s[^1]:0.###} s to the end");
        var lines = c.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != s.Count || lines.Select((line, i) => line.EndsWith($" Chapter {i + 1}", StringComparison.Ordinal)).Contains(false))
            p.Add("the lines aren't a chapter each");
        return p;
    }

    static string Show(string? text) => text is null ? "none" : $"\"{text.Replace("\n", "\\n")}\"";
}
