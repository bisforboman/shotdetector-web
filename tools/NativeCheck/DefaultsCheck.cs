// The page's defaults (README, "Checks"): the values the form prefills, read from the page's app.js
// (DEFAULT_THRESHOLDS, DEFAULT_WINDOW, DEFAULT_MIN_CONTENT), against ShotDetector's own. The page sends a value left at
// its default as none, so each must be the one a run without it uses: for each sample and detector, the run with the
// page's default typed in must give the run without it, cut for cut. Seen 2026-10-26: the form prefilled histogram 0.05
// and hash 0.395 (scenedetect 0.7.1's, which ShotDetector's Threshold summary said until 3.21.0) where the runs
// compared against 0.2 and 0.35, so a visitor who typed 0.05 got 0.2. The synthetic samples' hard cuts pass either
// way; Sintel's trailer tells them apart. A Markdown table on standard output.
using System.Globalization;
using System.Text.RegularExpressions;
using ShotDetector;

namespace ShotDetectorWeb.Checks;

static partial class DefaultsCheck
{
    /// <summary>Checks each sample with each of the page's defaults; returns how many disagreed.</summary>
    public static int Run(IReadOnlyList<string> samples)
    {
        string page = File.ReadAllText(AppJs());
        var thresholds = Thresholds().Match(page) is { Success: true } m
            ? Pair().Matches(m.Groups[1].Value).ToDictionary(p => p.Groups[1].Value, p => Number(p.Groups[2].Value))
            : throw new InvalidOperationException("app.js has no DEFAULT_THRESHOLDS.");
        int window = (int)Constant(page, "DEFAULT_WINDOW");
        double minContent = Constant(page, "DEFAULT_MIN_CONTENT");

        var typed = thresholds.Select(t => ($"{t.Key} {t.Value.ToString(CultureInfo.InvariantCulture)}",
                new DetectionOptions { Detector = Enum.Parse<DetectorKind>(t.Key, ignoreCase: true) },
                new DetectionOptions { Detector = Enum.Parse<DetectorKind>(t.Key, ignoreCase: true), Threshold = t.Value }))
            .Append(($"adaptive window {window}, min content_val {minContent.ToString(CultureInfo.InvariantCulture)}",
                new DetectionOptions { Detector = DetectorKind.Adaptive },
                new DetectionOptions { Detector = DetectorKind.Adaptive, FrameWindow = window, MinContentVal = minContent }))
            .ToList();

        int failures = 0;
        Console.WriteLine();
        Console.WriteLine($"| Sample | {string.Join(" | ", typed.Select(t => t.Item1))} |");
        Console.WriteLine($"| --- |{string.Concat(typed.Select(_ => " --- |"))}");
        foreach (string sample in samples)
        {
            var cells = new List<string>();
            foreach (var (_, library, page_) in typed)
            {
                var a = Mp4Input.Open(sample).Detect(library);
                var b = Mp4Input.Open(sample).Detect(page_);
                bool same = a.Cuts.SequenceEqual(b.Cuts) && a.Shots.Count == b.Shots.Count;
                cells.Add(same ? $"same ({a.Shots.Count} shots)" : $"DIFFERS: {b.Shots.Count} shots, the default run {a.Shots.Count}");
                if (!same)
                    failures++;
            }

            Console.WriteLine($"| {Path.GetFileName(sample)} | {string.Join(" | ", cells)} |");
        }

        return failures;
    }

    // The page's app.js, found from the repository root (the folder with Directory.Packages.props) above this program.
    static string AppJs()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
                return Path.Combine(dir.FullName, "src", "ShotDetectorWeb.App", "wwwroot", "app.js");
        }

        throw new InvalidOperationException("No repository root (Directory.Packages.props) above NativeCheck.");
    }

    static double Constant(string page, string name) =>
        Regex.Match(page, $@"\b{name} = ([0-9.]+)") is { Success: true } m
            ? Number(m.Groups[1].Value)
            : throw new InvalidOperationException($"app.js has no {name}.");

    static double Number(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"const DEFAULT_THRESHOLDS = \{([^}]*)\}")]
    private static partial Regex Thresholds();

    [GeneratedRegex(@"(\w+):\s*([0-9.]+)")]
    private static partial Regex Pair();
}
