// Corrected cuts (issue #90, decision 2026-11-02-correcting-cuts.md): every sample's default run with its cuts
// corrected as a viewer would on the page (Edit: one removed, one moved a frame, two added a frame apart, all frames
// counted from 0 as decoded), made into a result by the core (CorrectedCuts.Load: ShotDetector's LoadScenes of the run,
// 3.28.0) and by the same LoadScenes of the FFmpeg path's run (Detect(path)). The two must give the same downloads byte
// for byte: the scenes CSV, the shots as JSON and HTML, the editors' timeline files and the YouTube chapters; the shots
// must start at the frames given and the last end where the run's did; and the run's own cuts given back
// (DetectionResult.CutFrames) must give the run's own downloads. The same again for a run stopped half-way (the core's
// frames ended there, as the page's Stop does, against the FFmpeg path stopped by its end time at the same frame: since
// ShotDetector 3.29.0 both end it at the last frame's time plus a frame at the average rate, given every frame's
// timestamp, YuvVideo.FrameTimestamps), and whole under the run with every detector,
// deinterlaced (the options LoadScenes is given are the run's). The core's files, the run's cuts and the corrected
// starts of the whole run go to --out: app-check.mjs gives the starts to the App's Correct and writes its downloads
// beside them, which tools/check.sh compares; page-check.mjs corrects the run's cuts to them on the page.
using System.Text.Json;
using ShotDetector;
using ShotDetector.FastYuv;
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
        Console.WriteLine("| Sample | Corrected | Shots | Scenes CSV, shots JSON, HTML | Timelines, chapters | The run's cuts given back |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            var input = FfmpegDecoder.Open(sample);
            var deinterlaced = Runs.All.Single(r => r.Name == "deinterlace-on").Options;
            var detected = input.Detect(Options);
            int half = detected.FrameCount / 2;
            foreach (var (label, run, end, written) in new[]
            {
                ("whole", Options, (int?)null, true), ("stopped half-way", Options, half, false),
                ("whole, every detector deinterlaced", deinterlaced, null, false),
            })
            {
                var ours = end is { } stop
                    ? input.DetectAsync(1, given => ValueTask.FromResult(given < stop), run).GetAwaiter().GetResult()
                    : ReferenceEquals(run, Options) ? detected : input.Detect(run);
                // The FFmpeg path's run ends at the core's stopped frame: EndTime that frame's own time (its pts, the
                // first frame not read), as ShotDetector's FrameInputTests stop it, to the microsecond below so that
                // the frame itself isn't read (at a variable rate a frame number over the average rate is another frame).
                var file = FilePath(sample, run with { EndTime = end is { } at ? EndTimeOf(input.Video, at) : null }, libs);
                var edited = Edit([0, .. ours.CutFrames], ours.FrameCount);
                string name = Path.Combine(outDir, Runs.BaseName(sample, written ? "corrected" : end is null ? "corrected-deinterlaced" : "corrected-stopped"));
                if (written)
                {
                    File.WriteAllText(name + ".starts.json", JsonSerializer.Serialize(edited));
                    File.WriteAllText(name + ".cuts.json", JsonSerializer.Serialize(ours.CutFrames));
                }

                var corrected = CorrectedCuts.Load(ours, edited, run);
                var fileCorrected = CorrectedCuts.Load(file, edited, run);
                var differ = new List<string>();
                if (end is { } stopped && file.FrameCount != stopped)
                    differ.Add($"the FFmpeg path read {file.FrameCount} frames, not {stopped}");
                foreach (var (what, extension, a, b) in Downloads(corrected, fileCorrected, sample))
                {
                    // The default run's whole, which app-check.mjs writes too (tools/check.sh compares the folders); the
                    // others only where they differ, with the FFmpeg path's beside them.
                    if (written || a != b)
                        File.WriteAllText($"{name}.{extension}", a);
                    if (a == b)
                        continue;
                    differ.Add(what);
                    File.WriteAllText($"{name}.ffmpeg.{extension}", b);
                }

                // The list given is the list made (a cut a frame after another kept: the run's minimum shot length
                // applies only with DropShortScenes or MergeLastScene, which the page doesn't set), and the last shot
                // ends where the run's did (LoadScenes of a scene list ended at nb_frames, before 3.28.0's overload).
                // Each frame once: a run's cut can fall between two frames at a variable rate (the threshold detector's
                // fade cut, at its middle by the average rate: Elephants Dream's 428.508 s, in a 0.375 s gap before frame
                // 10157), where CutFrames gives the next frame, the one the run's next cut is on too, and LoadScenes
                // keeps the run's cuts at a frame given.
                if (!corrected.CutFrames.Distinct().SequenceEqual(edited.Where(s => s > 0)))
                    differ.Add($"shots start at {string.Join(",", corrected.CutFrames)}, not {string.Join(",", edited.Skip(1))}");
                if (!ours.CutFrames.SequenceEqual(file.CutFrames))
                    differ.Add($"the run's cuts {string.Join(",", ours.CutFrames)}, the FFmpeg path's {string.Join(",", file.CutFrames)}");
                FrameTime? length = corrected.Shots.Count > 0 ? corrected.Shots[^1].End : null;
                FrameTime? runLength = ours.Shots.Count > 0 ? ours.Shots[^1].End : null;
                if (length != runLength)
                    differ.Add($"the last shot ends at {length}, the run's at {runLength}");

                // The run's own cuts given back: the run's own downloads.
                var back = Downloads(CorrectedCuts.Load(ours, [0, .. ours.CutFrames], run), ours, sample)
                    .Where(d => d.A != d.B).Select(d => d.What).ToList();

                string[] files = ["scenes CSV", "JSON", "HTML"];
                var filesDiffer = differ.Where(files.Contains).ToList();
                var restDiffer = differ.Where(d => !files.Contains(d)).ToList();
                string first = filesDiffer.Count == 0 ? "same" : $"DIFFERS: {string.Join(", ", filesDiffer)}";
                string rest = restDiffer.Count == 0 ? "same" : $"DIFFERS: {string.Join(", ", restDiffer)}";
                string given = back.Count == 0 ? "the run's" : $"DIFFERS: {string.Join(", ", back)}";
                if (differ.Count > 0 || back.Count > 0)
                    failures++;
                Console.WriteLine($"| {Path.GetFileName(sample)} | {label} | {corrected.Shots.Count} | {first} | {rest} | {given} |");
            }
        }

        return failures;
    }

    /// <summary>The downloads of <paramref name="a"/> and <paramref name="b"/>, each named and with its file's extension.</summary>
    static IEnumerable<(string What, string Extension, string A, string B)> Downloads(DetectionResult a, DetectionResult b, string sample)
    {
        var (scenes, _) = Runs.Csvs(a);
        var (otherScenes, _) = Runs.Csvs(b);
        var (json, html) = Runs.ShotFiles(a);
        var (otherJson, otherHtml) = Runs.ShotFiles(b);
        yield return ("scenes CSV", "scenes.csv", scenes, otherScenes);
        yield return ("JSON", "shots.json", json, otherJson);
        yield return ("HTML", "shots.html", html, otherHtml);
        foreach (var (format, extension) in TimelineFiles.Formats)
        {
            string Write(DetectionResult r)
            {
                try { return TimelineFiles.Write(r, format, Path.GetFileName(sample)); }
                catch (InvalidOperationException e) { return "refused: " + e.Message; }
            }

            yield return (format, extension, Write(a), Write(b));
        }

        foreach (int minimum in ChaptersCheck.Minimums)
            yield return ($"chapters {minimum} s", $"chapters-{minimum}.json", TimelineFiles.Chapters(a, minimum), TimelineFiles.Chapters(b, minimum));
    }

    /// <summary>
    /// Frame <paramref name="frame"/>'s time from the video's start (its pts in <see cref="YuvVideo.FrameTimestamps"/>),
    /// as an EndTime in seconds rounded down to six decimals: the file path then reads the frames before it, not it.
    /// </summary>
    static string EndTimeOf(YuvVideo video, int frame)
    {
        var tb = video.TimeBase!.Value;
        double seconds = (video.FrameTimestamps![frame] - video.StartPts) * tb.Num / (double)tb.Den;
        return (Math.Floor(seconds * 1_000_000) / 1_000_000).ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The FFmpeg path's run of <paramref name="sample"/>, as NativeCheck's own (Program.cs): FFmpeg in-process.</summary>
    static DetectionResult FilePath(string sample, DetectionOptions options, string libs) =>
        ShotDetection.Detect(sample, options with { FfmpegDirectory = libs, Decoder = VideoDecoder.InProcess, Yuv420Converter = new SwscaleYuv420() });

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
