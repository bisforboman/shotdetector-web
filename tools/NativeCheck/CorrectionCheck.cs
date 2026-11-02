// Corrected cuts (issue #90, decision 2026-11-02-correcting-cuts.md): every sample's default run with its cuts
// corrected as a viewer would on the page (Edit: one removed, one moved a frame, two added a frame apart), made into a
// result by the core (CorrectedCuts.Load: ShotDetector's LoadScenes for a YuvVideo) and by ShotDetector's FFmpeg path
// (LoadScenes(path) on the same scene list). The two must give the same downloads byte for byte: the scenes CSV, the
// shots as JSON and HTML, the editors' timeline files and the YouTube chapters. The same again for a run stopped half-way
// (EndTime), and whole under the run with every detector, deinterlaced (the options LoadScenes is given are the
// run's). The core's files and the corrected starts of the whole run go to --out: app-check.mjs gives the starts to
// the App's Correct and writes its downloads beside them, which tools/check.sh compares.
using System.Text.Json;
using ShotDetector;
using ShotDetectorWeb;

namespace ShotDetectorWeb.Checks;

internal static class CorrectionCheck
{
    /// <summary>The run whose cuts are corrected: the page's default (the adaptive detector, scenedetect's defaults).</summary>
    static DetectionOptions Options => Runs.All[0].Options;

    public static int Run(IEnumerable<string> samples, string libs, string outDir)
    {
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Corrected | Shots | Scenes CSV, shots JSON, HTML | Timelines, chapters |");
        Console.WriteLine("| --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            var input = VideoInput.Open(sample);
            var detected = input.Detect(Options);
            var starts = detected.Shots.Select(s => (int)s.Start.FrameNum).ToList();
            var whole = Edit(starts, detected.FrameCount);
            int half = detected.FrameCount / 2;
            var stopped = Edit(starts.Where(s => s < half).ToList(), half);
            // The same list under a run with every detector, deinterlaced: the options LoadScenes is given are the run's.
            var deinterlaced = Runs.All.Single(r => r.Name == "deinterlace-on").Options;
            foreach (var (label, edited, end, run, written) in new[]
            {
                ("whole", whole, (int?)null, Options, true), ("stopped half-way", stopped, half, Options, false),
                ("whole, every detector deinterlaced", whole, null, deinterlaced, false),
            })
            {
                string name = Path.Combine(outDir, Runs.BaseName(sample, written ? "corrected" : end is null ? "corrected-deinterlaced" : "corrected-stopped"));
                if (written)
                    File.WriteAllText(name + ".starts.json", JsonSerializer.Serialize(edited));
                var ours = CorrectedCuts.Load(input.Video, edited, run, end);
                string list = Path.GetTempFileName();
                DetectionResult file;
                try
                {
                    File.WriteAllText(list, CorrectedCuts.SceneList(edited));
                    file = ShotDetection.LoadScenes(sample, list, CorrectedCuts.Column,
                        CorrectedCuts.Options(run, end) with { FfmpegDirectory = libs });
                }
                finally
                {
                    File.Delete(list);
                }

                var differ = new List<string>();
                void Compare(string what, string extension, string a, string b)
                {
                    // The default run's whole, which app-check.mjs writes too (tools/check.sh compares the folders); the
                    // others only where they differ.
                    if (written)
                        File.WriteAllText($"{name}.{extension}", a);
                    if (a == b)
                        return;
                    differ.Add(what);
                    File.WriteAllText($"{name}.ffmpeg.{extension}", b);
                }

                var (scenes, _) = Runs.Csvs(ours);
                var (fileScenes, _) = Runs.Csvs(file);
                var (json, html) = Runs.ShotFiles(ours);
                var (fileJson, fileHtml) = Runs.ShotFiles(file);
                Compare("scenes CSV", "scenes.csv", scenes, fileScenes);
                Compare("JSON", "shots.json", json, fileJson);
                Compare("HTML", "shots.html", html, fileHtml);
                foreach (var (format, extension) in TimelineFiles.Formats)
                {
                    string Write(DetectionResult r)
                    {
                        try { return TimelineFiles.Write(r, format, Path.GetFileName(sample)); }
                        catch (InvalidOperationException e) { return "refused: " + e.Message; }
                    }

                    Compare(format, extension, Write(ours), Write(file));
                }

                foreach (int minimum in ChaptersCheck.Minimums)
                    Compare($"chapters {minimum} s", $"chapters-{minimum}.json", TimelineFiles.Chapters(ours, minimum), TimelineFiles.Chapters(file, minimum));

                // The list given is the list made, up to where LoadScenes' video ends: it keeps a cut a frame after
                // another (the run's minimum shot length applies only with DropShortScenes or MergeLastScene, which the
                // page doesn't set). Its video ends at nb_frames (OpenCV's count), which can be before the frames
                // FFmpeg plays (an edit list that ends early, a fragmented file whose moov has some samples): noted, as
                // ShotDetector's (sent upstream 2026-11-02), not a failure while the FFmpeg path agrees.
                int length = ours.Shots.Count > 0 ? (int)ours.Shots[^1].End.FrameNum : 0;
                var made = ours.Shots.Select(s => (int)s.Start.FrameNum).ToList();
                if (!made.SequenceEqual(edited.Where(s => s < length)))
                    differ.Add($"shots start at {string.Join(",", made)}, not {string.Join(",", edited)}");
                int expectedEnd = end ?? detected.FrameCount;
                string note = length == expectedEnd ? "" : $" (LoadScenes ends at frame {length}, the run at {expectedEnd})";
                if (end is { } e && length > e)
                    differ.Add($"the last shot ends at frame {length}, past the stop at {e}");
                string[] files = ["scenes CSV", "JSON", "HTML"];
                var filesDiffer = differ.Where(files.Contains).ToList();
                var restDiffer = differ.Where(d => !files.Contains(d)).ToList();
                string first = filesDiffer.Count == 0 ? "same" : $"DIFFERS: {string.Join(", ", filesDiffer)}";
                string rest = restDiffer.Count == 0 ? "same" : $"DIFFERS: {string.Join(", ", restDiffer)}";
                if (differ.Count > 0)
                    failures++;
                Console.WriteLine($"| {Path.GetFileName(sample)} | {label}{note} | {ours.Shots.Count} | {first} | {rest} |");
            }
        }

        return failures;
    }

    /// <summary>
    /// The shots' first frames after a viewer's corrections, as the page makes them: the first cut removed, the last
    /// moved a frame later, a cut added in the middle of the longest shot and another a frame after it (a one-frame
    /// shot). <paramref name="starts"/> are a run's (frame 0 first), <paramref name="frames"/> how many it read.
    /// </summary>
    public static List<int> Edit(IReadOnlyList<int> starts, int frames)
    {
        var cuts = new SortedSet<int>(starts.Where(s => s > 0));
        if (cuts.Count > 0)
            cuts.Remove(cuts.Min);
        if (cuts.Count > 0 && cuts.Max + 1 < frames && !cuts.Contains(cuts.Max + 1))
        {
            int last = cuts.Max;
            cuts.Remove(last);
            cuts.Add(last + 1);
        }

        var bounds = cuts.Prepend(0).Append(frames).ToList();
        int longest = Enumerable.Range(0, bounds.Count - 1).MaxBy(i => bounds[i + 1] - bounds[i]);
        int middle = (bounds[longest] + bounds[longest + 1]) / 2;
        if (middle > bounds[longest] && middle + 1 < bounds[longest + 1])
        {
            cuts.Add(middle);
            cuts.Add(middle + 1);
        }

        return [0, .. cuts];
    }
}
