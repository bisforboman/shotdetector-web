using ShotDetector;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The range check (README, "Checks"): for each sample, the run with every detector (<see cref="Runs.All"/>' last) split
/// into ranges at keyframes as the page splits a long video across workers (<see cref="Mp4Input.Ranges"/>, at most 4),
/// each range scored on its own (<see cref="Mp4Input.ScoreRangeAsync"/>), carried as bytes and merged
/// (<see cref="Mp4Input.Merge"/>): the scene and stats CSVs must be the whole run's, which the main check wrote to the
/// output folder, byte for byte; and the frames the video shows (<see cref="Mp4Input.ShownFrames"/>, which the last
/// range runs to) must be the whole run's frame count.
/// </summary>
internal static class RangeCheck
{
    const int MostRanges = 4;

    /// <summary>Prints a table with a row per sample; returns how many samples differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string outDir)
    {
        int failures = 0;
        var (run, options) = Runs.All[^1];
        Console.WriteLine();
        Console.WriteLine($"| Sample | Ranges ({run}) | Shown frames | Against the whole run |");
        Console.WriteLine("| --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample), baseName = Path.Combine(outDir, Runs.BaseName(sample, run));
            try
            {
                var input = Mp4Input.Open(sample);
                var ranges = input.Ranges(MostRanges);
                var scores = ranges
                    .Select(r => RangeScores.FromBytes(input.ScoreRangeAsync(r, 8, _ => ValueTask.FromResult(true), options)
                        .GetAwaiter().GetResult().ToBytes()))
                    .ToList();
                var merged = input.Merge(Mp4Input.FromStart(scores), options);
                var (scenes, stats) = Runs.Csvs(merged);
                var problems = new List<string>();
                if (Mp4Input.FromStart(scores).Count != ranges.Count)
                    problems.Add("not every range follows the one before");
                if (scenes != File.ReadAllText(baseName + ".scenes.csv"))
                    problems.Add("the scenes CSV DIFFERS");
                if (stats != File.ReadAllText(baseName + ".stats.csv"))
                    problems.Add("the stats CSV DIFFERS");
                if (input.ShownFrames != merged.FrameCount)
                    problems.Add($"{merged.FrameCount} frames merged");
                failures += problems.Count > 0 ? 1 : 0;
                string split = string.Join(", ", ranges.Select(r => $"{r.FirstFrame}-{r.EndFrame - 1}"));
                Console.WriteLine($"| {name} | {split} | {input.ShownFrames} | {(problems.Count == 0 ? "same" : string.Join("; ", problems))} |");
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | - | - | failed: {e.GetType().Name}: {e.Message} |");
                failures++;
            }
        }

        return failures;
    }
}
