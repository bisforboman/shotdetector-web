using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The reference tone check (README, "Checks"; decision 2026-11-04-reference-tone.md): for each sample whose audio has
/// reference tone as the core finds it (<see cref="Mp4Audio"/>, the page's sound), the run with every detector and
/// <see cref="DetectionOptions.TrimReferenceTone"/> (<see cref="Runs.Tone"/>), the core given that tone
/// (<see cref="VideoInput.ReferenceTone"/>, as the page gives it), against ShotDetector's <c>Detect(path)</c> with FFmpeg
/// finding the tone itself: the scene and stats CSVs, the shots as JSON and HTML and the tone the result reports must be
/// equal byte for byte. Then the same run split into ranges at keyframes (at most 4) and merged; stopped past a third and
/// two thirds on one range and resumed; and split into 4, each stopped past 60%, the rest split again: each the whole
/// run's. Writes the run's files to the output folder for the wasm check.
/// </summary>
internal static class ToneCheck
{
    /// <summary>Prints a table with a row per sample with tone; returns how many differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string outDir, string libs)
    {
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Tone (the core's) | Shots from | Shots | Against Detect(path) | Split | Stopped, resumed | Split, stopped, resumed |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample);
            try
            {
                if (!sample.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    continue;
                var measures = Mp4Audio.Measure(sample);
                bool expected = name.StartsWith("tone-", StringComparison.Ordinal);
                if (measures.ReferenceTone.Count == 0)
                {
                    if (expected)
                    {
                        Console.WriteLine($"| {name} | none found | - | - | the tone sample has no tone | - | - | - |");
                        failures++;
                    }

                    continue;
                }

                var (run, options) = Runs.Tone;
                string baseName = Path.Combine(outDir, Runs.BaseName(sample, run));
                var input = FfmpegDecoder.Open(sample);
                input.ReferenceTone = measures.ReferenceTone;
                var ours = input.Detect(options);
                var file = ShotDetection.Detect(sample, options with { FfmpegDirectory = libs, Decoder = VideoDecoder.InProcess, Yuv420Converter = new SwscaleYuv420() });

                var (scenes, stats) = Runs.Csvs(ours);
                var (json, html) = Runs.ShotFiles(ours);
                File.WriteAllText(baseName + ".scenes.csv", scenes);
                File.WriteAllText(baseName + ".stats.csv", stats);
                File.WriteAllText(baseName + ".shots.json", json);
                File.WriteAllText(baseName + ".shots.html", html);

                var problems = new List<string>();
                var (fileScenes, fileStats) = Runs.Csvs(file);
                var (fileJson, fileHtml) = Runs.ShotFiles(file);
                if (scenes != fileScenes)
                    problems.Add("the scenes CSV DIFFERS");
                if (stats != fileStats)
                    problems.Add("the stats CSV DIFFERS");
                if (json != fileJson || html != fileHtml)
                    problems.Add("the shots' JSON or HTML DIFFERS");
                if (!Segments(ours.ReferenceTone).SequenceEqual(Segments(file.ReferenceTone)))
                    problems.Add($"the tone reported {Shown(ours.ReferenceTone)}, the FFmpeg path's {Shown(file.ReferenceTone)}");
                if (problems.Count > 0)
                {
                    File.WriteAllText(baseName + ".ffmpeg.scenes.csv", fileScenes);
                    File.WriteAllText(baseName + ".ffmpeg.stats.csv", fileStats);
                }

                // Where the shots start: after the leading tone, where the FFmpeg path's seek lands.
                string start = ours.Shots.Count > 0 ? $"{ours.Shots[0].Start.Seconds:0.###} s" : "-";
                string split = Split(input, options, baseName);
                string resumed = Resumed(input, options, baseName);
                string splitResumed = SplitResumed(input, options, baseName);
                failures += problems.Count > 0 || split != "same" || resumed != "same" || splitResumed != "same" ? 1 : 0;
                Console.WriteLine($"| {name} | {Shown(measures.ReferenceTone)} | {start} | {ours.Shots.Count} | " +
                    $"{(problems.Count == 0 ? "same" : string.Join("; ", problems))} | {split} | {resumed} | {splitResumed} |");
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | - | - | - | failed: {e.GetType().Name}: {e.Message} | - | - | - |");
                failures++;
            }
        }

        return failures;
    }

    // Split into at most 4 ranges at keyframes, each scored on its own and carried as bytes, then merged.
    static string Split(VideoInput input, DetectionOptions options, string baseName)
    {
        var ranges = input.Ranges(options, 4);
        var scores = ranges.Select(r => Carried(input.ScoreRangeAsync(r, 8, _ => ValueTask.FromResult(true), options)
            .GetAwaiter().GetResult())).ToList();
        return Same(input.Merge(input.FromStart(scores), options), baseName, $"{ranges.Count} ranges: ");
    }

    // One range from frame 0, stopped past a third and two thirds, resumed each time (the page's one-worker run).
    static string Resumed(VideoInput input, DetectionOptions options, string baseName)
    {
        int frames = input.ShownFrames;
        var kept = new List<RangeScores>();
        var at = new List<int>();
        foreach (int stop in new[] { frames / 3, frames * 2 / 3, int.MaxValue })
        {
            var go = input.Continue(kept, options);
            if (go.Rest is not { } rest)
                break;
            at.Add(rest.FirstFrame);
            int from = rest.From;
            var scored = input.ScoreRangeAsync(go.Earlier, rest, 8, given => ValueTask.FromResult(from + given < stop), options)
                .GetAwaiter().GetResult();
            kept = [.. go.Earlier.Select(Carried), Carried(scored)];
        }

        return Same(input.Merge(input.FromStart(kept), options), baseName, $"at {string.Join(", ", at)}: ");
    }

    // Split into 4, each stopped past 60% of its frames; the rest split into 4 again from the continuation's first frame.
    static string SplitResumed(VideoInput input, DetectionOptions options, string baseName)
    {
        var stopped = input.Ranges(options, 4).Select(r =>
        {
            int from = r.From, stop = r.FirstFrame + (r.EndFrame - r.FirstFrame) * 6 / 10;
            return Carried(input.ScoreRangeAsync(r, 8, given => ValueTask.FromResult(from + given < stop), options)
                .GetAwaiter().GetResult());
        }).ToList();
        var go = input.Continue(stopped, options);
        if (go.Rest is not { } rest)
            return "nothing left to resume";
        var scored = input.Ranges(options, 4, rest.FirstFrame)
            .Select(r => Carried(input.ScoreRangeAsync(r, 8, _ => ValueTask.FromResult(true), options).GetAwaiter().GetResult()));
        return Same(input.Merge(input.FromStart([.. go.Earlier, .. scored]), options), baseName, "");
    }

    static RangeScores Carried(RangeScores scores) => RangeScores.FromBytes(scores.ToBytes());

    static string Same(DetectionResult result, string baseName, string what)
    {
        var (scenes, stats) = Runs.Csvs(result);
        var (json, html) = Runs.ShotFiles(result);
        var problems = new List<string>();
        if (scenes != File.ReadAllText(baseName + ".scenes.csv"))
            problems.Add("the scenes CSV DIFFERS");
        if (stats != File.ReadAllText(baseName + ".stats.csv"))
            problems.Add("the stats CSV DIFFERS");
        if (json != File.ReadAllText(baseName + ".shots.json") || html != File.ReadAllText(baseName + ".shots.html"))
            problems.Add("the shots' JSON or HTML DIFFERS");
        return problems.Count == 0 ? "same" : what + string.Join("; ", problems);
    }

    static IEnumerable<(TimeSpan, TimeSpan)> Segments(IReadOnlyList<FrameReader.ToneSegment>? tone) =>
        (tone ?? []).Select(s => (s.Start, s.End));

    static string Shown(IReadOnlyList<FrameReader.ToneSegment>? tone) =>
        tone is null ? "none looked for" : tone.Count == 0 ? "none" : string.Join(", ", tone.Select(s => $"{s.Start.TotalSeconds:0.###}-{s.End.TotalSeconds:0.###} s"));
}
