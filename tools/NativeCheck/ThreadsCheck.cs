using ShotDetector;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The threads check (README, "Checks"; decision 2026-10-30-threads.md): ManagedCodecs' decoder on
/// <see cref="Threads"/> threads (<see cref="Mp4Input.DecoderThreads"/>, as the page's threaded runtime decodes) must
/// give every sample's run with every detector (<see cref="Runs.All"/>' last; all of them read the same frames) the
/// one-thread run's CSVs, which the main check wrote, byte for byte; so must its ranges scored and merged
/// (<see cref="Mp4Input.ScoreRangeAsync"/>, as RangeCheck), and the run stopped half-way the one-thread run stopped
/// there (its frames ended early, the decoder's pictures still in flight drained).
/// </summary>
internal static class ThreadsCheck
{
    const int Threads = 4;

    /// <summary>Prints a table with a row per sample; returns how many samples differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string outDir)
    {
        int failures = 0;
        var (run, options) = Runs.All[^1];
        Console.WriteLine();
        Console.WriteLine($"| Sample | {Threads} decoder threads ({run}) | Ranges | Stopped half-way |");
        Console.WriteLine("| --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample), baseName = Path.Combine(outDir, Runs.BaseName(sample, run));
            try
            {
                var input = Mp4Input.Open(sample);
                input.DecoderThreads = Threads;
                string whole = Same(input.Detect(options), baseName);

                var scores = input.Ranges(4)
                    .Select(r => input.ScoreRangeAsync(r, 8, _ => ValueTask.FromResult(true), options).GetAwaiter().GetResult())
                    .ToList();
                string ranges = Same(input.Merge(Mp4Input.FromStart(scores), options), baseName);

                string stopped = Stopped(input, options);
                bool ok = whole == "same" && ranges == "same" && stopped.StartsWith("same", StringComparison.Ordinal);
                failures += ok ? 0 : 1;
                Console.WriteLine($"| {name} | {whole} | {ranges} | {stopped} |");
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | failed: {e.GetType().Name}: {e.Message} | - | - |");
                failures++;
            }
        }

        return failures;
    }

    static string Same(DetectionResult result, string baseName)
    {
        var (scenes, stats) = Runs.Csvs(result);
        var problems = new List<string>();
        if (scenes != File.ReadAllText(baseName + ".scenes.csv"))
            problems.Add("the scenes CSV DIFFERS");
        if (stats != File.ReadAllText(baseName + ".stats.csv"))
            problems.Add("the stats CSV DIFFERS");
        return problems.Count == 0 ? "same" : string.Join("; ", problems);
    }

    // The run stopped at the first pause past half its frames, on the threads and on one: the same CSVs.
    static string Stopped(Mp4Input input, DetectionOptions options)
    {
        int half = input.ShownFrames / 2;
        ValueTask<bool> Between(int read) => ValueTask.FromResult(read < half);
        var threaded = input.DetectAsync(8, Between, options).GetAwaiter().GetResult();
        input.DecoderThreads = 1;
        var one = input.DetectAsync(8, Between, options).GetAwaiter().GetResult();
        input.DecoderThreads = Threads;
        return Runs.Csvs(threaded) == Runs.Csvs(one) && threaded.FrameCount == one.FrameCount
            ? $"same ({one.FrameCount} frames)"
            : $"DIFFERS ({threaded.FrameCount} frames on {Threads} threads, {one.FrameCount} on one)";
    }
}
