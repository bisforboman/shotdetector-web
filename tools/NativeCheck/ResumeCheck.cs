using ShotDetector;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The resume check (README, "Checks"; backlog "Resume after Stop, across visits"): for each sample, the adaptive run
/// (<see cref="Runs.All"/>' first, the page's default) and the run with every detector (<see cref="Runs.Every"/>), each scored as the
/// page's one-worker run scores (one range from frame 0) and stopped at the first pause past a third of the frames, its
/// scores carried as bytes (as the page keeps them in IndexedDB), resumed from there (<see cref="VideoInput.Continue"/>,
/// the earlier ranges given to <see cref="VideoInput.ScoreRangeAsync(IReadOnlyList{RangeScores}, FrameRange, int, Func{int, ValueTask{bool}}, DetectionOptions?, CancellationToken, PictureMeter?)"/>),
/// stopped again past two thirds and resumed again to the end, then merged: the CSVs, shots' JSON and HTML must be the
/// whole run's, which the main check wrote, byte for byte, and the picture measures joined as the page joins them the
/// whole run's. The shots told while the parts run (<see cref="DetectionOptions.OnShot"/>), after the stopped parts'
/// shots up to the first one told again, must be the whole run's. And the run split into 4 ranges, each stopped
/// past 60% of its frames, resumed with the rest split into 4 again (<see cref="VideoInput.Ranges"/> from the
/// continuation's first frame), must merge into the whole run's CSVs too.
/// </summary>
internal static class ResumeCheck
{
    /// <summary>Prints a table with a row per sample and run; returns how many differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string outDir)
    {
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Run | Stopped, resumed at | Against the whole run | Shots told | Picture | Split, resumed |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            foreach (var (run, options) in new[] { Runs.All[0], Runs.Every })
            {
                string name = Path.GetFileName(sample), baseName = Path.Combine(outDir, Runs.BaseName(sample, run));
                try
                {
                    var input = VideoInput.Open(sample);
                    int frames = input.ShownFrames;
                    var told = new List<Shot>();
                    var telling = options with { OnShot = new Told(told) };
                    var kept = new List<RangeScores>();
                    var pictures = new List<PictureMeasures>();
                    var shown = new List<Shot>(); // the shots the page shows: the stopped parts', then those told again
                    var at = new List<string>();
                    foreach (int stop in new[] { frames / 3, frames * 2 / 3, int.MaxValue })
                    {
                        var go = input.Continue(kept);
                        if (go.Rest is not { } rest)
                            break;
                        at.Add(rest.FirstFrame.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        // The frames given count from the one before the range's first.
                        int from = Math.Max(rest.FirstFrame - 1, 0);
                        var picture = new PictureMeter(input.Video, rest);
                        int first = told.Count;
                        var scored = input.ScoreRangeAsync(go.Earlier, rest, 8, given => ValueTask.FromResult(from + given < stop),
                            telling, picture: picture).GetAwaiter().GetResult();
                        if (told.Count > first)
                        {
                            shown.RemoveAll(s => s.Number >= told[first].Number);
                            shown.AddRange(told.Skip(first));
                        }

                        kept = [.. go.Earlier.Select(Carried), Carried(scored)];
                        pictures = go.Earlier.Count == 0 ? [picture.Measures] : [Joined(pictures, rest.FirstFrame), picture.Measures];
                    }

                    var merged = input.Merge(VideoInput.FromStart(kept), options);
                    var problems = Same(merged, baseName);
                    if (merged.FrameCount != frames)
                        problems.Add($"{merged.FrameCount} frames merged");
                    string shots = Shots.Json(shown) == Shots.Json(merged.Shots) ? "the whole run's" : "DIFFER";
                    if (shots != "the whole run's")
                        problems.Add("the shots told differ");

                    var whole = new PictureMeter(input.Video);
                    input.DetectAsync(8, _ => ValueTask.FromResult(true), options, picture: whole).GetAwaiter().GetResult();
                    bool samePicture = Joined(pictures, frames).ToBytes().AsSpan().SequenceEqual(whole.Measures.ToBytes());
                    if (!samePicture)
                        problems.Add("the picture measures differ");

                    string split = Split(input, options, baseName);
                    if (split != "same")
                        problems.Add("the split run resumed differs");
                    failures += problems.Count > 0 ? 1 : 0;
                    string result = problems.Count == 0 ? "same" : string.Join("; ", problems);
                    Console.WriteLine($"| {name} | {run} | {string.Join(", ", at)} | {result} | {shots} | {(samePicture ? "same" : "DIFFERS")} | {split} |");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"| {name} | {run} | - | failed: {e.GetType().Name}: {e.Message} | - | - | - |");
                    failures++;
                }
            }
        }

        return failures;
    }

    // Split into 4 ranges, each stopped past 60% of its own frames; the ranges from the start kept, the rest split into 4
    // again from the continuation's first frame (no OnShot: the page's split runs tell their shots from the merge).
    static string Split(VideoInput input, DetectionOptions options, string baseName)
    {
        var stopped = input.Ranges(4).Select(r =>
        {
            int from = Math.Max(r.FirstFrame - 1, 0), stop = r.FirstFrame + (r.EndFrame - r.FirstFrame) * 6 / 10;
            return Carried(input.ScoreRangeAsync(r, 8, given => ValueTask.FromResult(from + given < stop), options)
                .GetAwaiter().GetResult());
        }).ToList();
        var go = input.Continue(stopped);
        if (go.Rest is not { } rest)
            return "nothing left to resume";
        var scored = input.Ranges(4, rest.FirstFrame)
            .Select(r => Carried(input.ScoreRangeAsync(r, 8, _ => ValueTask.FromResult(true), options).GetAwaiter().GetResult()));
        var problems = Same(input.Merge(VideoInput.FromStart([.. go.Earlier, .. scored]), options), baseName);
        return problems.Count == 0 ? "same" : string.Join("; ", problems);
    }

    // Scores as the page carries them: as bytes, through IndexedDB or a worker's message.
    static RangeScores Carried(RangeScores scores) => RangeScores.FromBytes(scores.ToBytes());

    // The measures so far, joined from frame 0 and cut where the next part starts (it measures the frames from there).
    static PictureMeasures Joined(IReadOnlyList<PictureMeasures> parts, int end) => PictureMeasures.Join(parts)!.Take(end);

    static List<string> Same(DetectionResult result, string baseName)
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
        return problems;
    }

    // Not Progress<T>: that would post to a synchronization context.
    sealed class Told(List<Shot> shots) : IProgress<Shot>
    {
        public void Report(Shot value) => shots.Add(value);
    }
}
