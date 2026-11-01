// The picture check (README, "Checks"; decision 2026-10-30-picture-measures.md): for each sample, the picture measures
// of the default run (PictureMeter, alongside detection) against FFmpeg's filters on the same file, exactly: the frames
// are FFmpeg's (the CSVs prove it) and the filters count integers over them.
// The 10-bit samples as the others: FFmpeg's filters take yuv420p10le as it is and count 10-bit code values (their
// thresholds by the depth), as PictureMeter does.
// - signalstats=stat=brng per frame: YMIN and YMAX equal, YAVG and BRNG to the digits FFmpeg prints (%g);
// - cropdetect=reset=1:skip=0 per frame: x1, x2, y1, y2, w, h, x, y equal; and cropdetect with its defaults (reset=0,
//   skip=2, round=16) after the last frame, its crop equal;
// - blackdetect with d=0 and with its default 2 s: the same spans, their times to the digits FFmpeg prints (-copyts:
//   the stream's own pts, as ours);
// - the colour bars, which no FFmpeg filter finds, by their definition: on the samples whose last shot is smptehdbars
//   (make-samples.sh's three(), the interlaced ones woven, the rotated ones upright, the
//   variable-rate ones at their own timestamps, the 10-bit ones in 10-bit levels), one span, the last shot's frames (from the default run's last cut; decoded frame 100 on for
//   the variable-rate ones, whose cuts are numbered by time at the average rate); on the fades sample
//   (smptebars fading in for 0.3 s), one span to the end, starting within the fade; on the others, none.
// And the run split into ranges as the page splits it (at most 4), each measured by its own meter while it is scored:
// joined, their measures must be the whole run's, byte for byte. Each sample's summary (the App's Picture() JSON) goes
// to the output folder as <sample>.picture.json, which the wasm check compares its own with.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FrameReader;
using ShotDetector;

namespace ShotDetectorWeb.Checks;

static class PictureCheck
{
    const int MostRanges = 4;

    /// <summary>Checks each sample; returns how many disagreed or failed.</summary>
    public static int Run(IReadOnlyList<string> samples, string libs, string outDir)
    {
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Frames | signalstats (per frame) | cropdetect (per frame; whole) | blackdetect (d=0; 2 s) | Colour bars | Ranges | Core time |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            var problems = new List<string>();
            var cells = new List<string>();
            try
            {
                Check(sample, libs, outDir, problems, cells);
            }
            catch (Exception e)
            {
                problems.Add($"{e.GetType().Name}: {e.Message}");
            }

            if (problems.Count > 0)
                failures++;
            while (cells.Count < 7)
                cells.Add("");
            if (problems.Count > 0)
                cells[0] += " **DIFFERS: " + string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? $"; {problems.Count - 5} more" : "") + "**";
            Console.WriteLine($"| {Path.GetFileName(sample)} | {string.Join(" | ", cells)} |");
        }

        return failures;
    }

    static void Check(string sample, string libs, string outDir, List<string> problems, List<string> cells)
    {
        var input = VideoInput.Open(sample);
        var meter = new PictureMeter(input.Video);
        var watch = Stopwatch.StartNew();
        var result = input.Detect(new DetectionOptions(), picture: meter);
        var time = watch.Elapsed;
        var ours = meter.Measures;
        if (ours.Count != result.FrameCount)
            problems.Add($"{ours.Count} frames measured, the run read {result.FrameCount}");
        cells.Add($"{ours.Count}");

        var theirs = Ffmpeg(libs, sample);
        if (theirs.Frames.Count != ours.Count)
            problems.Add($"FFmpeg's filters saw {theirs.Frames.Count} frames");

        // signalstats and cropdetect, frame by frame.
        int stats = 0, crops = 0;
        for (int i = 0; i < Math.Min(ours.Count, theirs.Frames.Count); i++)
        {
            var f = theirs.Frames[i];
            if (ours.YMin(i) != int.Parse(f["lavfi.signalstats.YMIN"]) || ours.YMax(i) != int.Parse(f["lavfi.signalstats.YMAX"]) ||
                !Printed(f["lavfi.signalstats.YAVG"], ours.YAvg(i)) || !Printed(f["lavfi.signalstats.BRNG"], ours.Brng(i)))
            {
                if (stats++ < 2)
                {
                    problems.Add($"frame {i}: YMIN {ours.YMin(i)} YAVG {ours.YAvg(i)} YMAX {ours.YMax(i)} BRNG {ours.Brng(i)}, FFmpeg's " +
                        $"{f["lavfi.signalstats.YMIN"]} {f["lavfi.signalstats.YAVG"]} {f["lavfi.signalstats.YMAX"]} {f["lavfi.signalstats.BRNG"]}");
                }
            }

            var c = ours.FrameCrop(i);
            int[] mine = [c.X1, c.X2, c.Y1, c.Y2, c.W, c.H, c.X, c.Y];
            int[] crop = new[] { "x1", "x2", "y1", "y2", "w", "h", "x", "y" }.Select(k => int.Parse(f["lavfi.cropdetect." + k])).ToArray();
            if (!mine.SequenceEqual(crop) && crops++ < 2)
                problems.Add($"frame {i}: cropdetect {string.Join(",", mine)}, FFmpeg's {string.Join(",", crop)}");
        }

        if (stats > 2)
            problems.Add($"signalstats differs on {stats} frames");
        if (crops > 2)
            problems.Add($"cropdetect differs on {crops} frames");
        cells.Add(stats == 0 ? "same" : $"{stats} frames differ");

        var whole = ours.Crop();
        if (whole?.ToString() != theirs.Crop)
            problems.Add($"cropdetect's crop {whole?.ToString() ?? "none"}, FFmpeg's {theirs.Crop ?? "none"}");
        cells.Add($"{(crops == 0 ? "same" : $"{crops} frames differ")}; {whole?.ToString() ?? "none"}");

        // blackdetect at d=0 and its default 2 s.
        var blackCells = new List<string>();
        foreach (var (d, theirSpans) in new[] { (0.0, theirs.Black0), (2.0, theirs.Black2) })
        {
            double tb = ours.TimeBase.Num / (double)ours.TimeBase.Den;
            var spans = ours.BlackSpans(d);
            bool same = spans.Count == theirSpans.Count && spans.Zip(theirSpans).All(p =>
                Printed(p.Second.Start, p.First.StartPts * tb) && Printed(p.Second.End, p.First.EndPts * tb));
            if (!same)
            {
                problems.Add($"blackdetect d={d}: {string.Join(", ", spans.Select(s => $"{s.StartPts * tb:0.######}-{s.EndPts * tb:0.######}"))}, " +
                    $"FFmpeg's {string.Join(", ", theirSpans.Select(s => $"{s.Start}-{s.End}"))}");
            }

            blackCells.Add(spans.Count == 0 ? "none" : string.Join(", ", spans.Select(s => $"{s.StartPts * tb:0.###}-{s.EndPts * tb:0.###} s")));
        }

        cells.Add(string.Join("; ", blackCells));
        cells.Add(Bars(sample, ours, result, problems));
        cells.Add(Ranges(input, ours, problems));
        cells.Add($"{time.TotalSeconds:F2} s");

        File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(sample) + ".picture.json"),
            JsonSerializer.Serialize(PictureSummary.Of(ours), PictureJson.Default.PictureSummary));
    }

    /// <summary>The colour bars against what make-samples.sh put in the sample (above).</summary>
    static string Bars(string sample, PictureMeasures ours, DetectionResult result, List<string> problems)
    {
        var spans = ours.BarsSpans();
        string name = Path.GetFileName(sample);
        string found = spans.Count == 0 ? "none" : string.Join(", ", spans.Select(s => $"frames {s.FirstFrame}-{s.EndFrame - 1}"));
        bool three = name.StartsWith("cuts-", StringComparison.Ordinal) || name.StartsWith("baseline-", StringComparison.Ordinal) ||
            name.StartsWith("full-range-", StringComparison.Ordinal) || name.StartsWith("start-offset-", StringComparison.Ordinal) ||
            name.StartsWith("edit-zero-", StringComparison.Ordinal) ||
            name.StartsWith("with-audio-", StringComparison.Ordinal) || name.StartsWith("fragmented-", StringComparison.Ordinal) ||
            name.StartsWith("interlaced-", StringComparison.Ordinal) || name.StartsWith("mjpeg-", StringComparison.Ordinal) ||
            name.StartsWith("rotated-", StringComparison.Ordinal) || name.StartsWith("vfr-", StringComparison.Ordinal) ||
            name.StartsWith("yuv422p-", StringComparison.Ordinal) || name.StartsWith("yuvj422p-", StringComparison.Ordinal) ||
            name.StartsWith("yuv420p10-", StringComparison.Ordinal);
        if (three)
        {
            // The variable-rate samples' last shot is decoded frame 100 on (two shots of 50 before it): the cut's FrameNum
            // is scenedetect's number for its time at the average rate (5.6 s at 1250/79 fps: 89), not the frame's index.
            int last = name.StartsWith("vfr-", StringComparison.Ordinal) ? 100
                : result.Cuts.Count > 0 ? (int)result.Cuts[^1].FrameNum : -1;
            if (spans.Count != 1 || spans[0].FirstFrame != last || spans[0].EndFrame != ours.Count)
                problems.Add($"colour bars {found}, not the last shot's frames {last}-{ours.Count - 1}");
            return $"{found} (the last shot)";
        }

        if (name.StartsWith("fades-", StringComparison.Ordinal))
        {
            int last = result.Cuts.Count > 0 ? (int)result.Cuts[^1].FrameNum : -1;
            double fps = (double)result.Video.FrameRate.Num / result.Video.FrameRate.Den;
            if (spans.Count != 1 || spans[0].EndFrame != ours.Count || spans[0].FirstFrame < last ||
                spans[0].FirstFrame > last + (int)Math.Ceiling(0.3 * fps))
                problems.Add($"colour bars {found}, not to the end from within the fade in at frame {last}");
            return $"{found} (fading in from {last})";
        }

        if (spans.Count != 0)
            problems.Add($"colour bars {found} in a sample without them");
        return found;
    }

    /// <summary>The run split into ranges, each measured while it is scored, joined: the whole run's measures.</summary>
    static string Ranges(VideoInput input, PictureMeasures whole, List<string> problems)
    {
        var ranges = input.Ranges(null, MostRanges);
        var parts = new List<PictureMeasures>();
        foreach (var range in ranges)
        {
            var meter = new PictureMeter(input.Video, range);
            input.ScoreRangeAsync(range, 8, _ => ValueTask.FromResult(true), new DetectionOptions(), picture: meter)
                .GetAwaiter().GetResult();
            parts.Add(PictureMeasures.FromBytes(meter.Measures.ToBytes()));
        }

        var joined = PictureMeasures.Join(parts);
        bool same = joined is not null && joined.ToBytes().AsSpan().SequenceEqual(whole.ToBytes());
        if (!same)
            problems.Add($"the {ranges.Count} ranges' measures joined ({joined?.Count ?? 0} frames) aren't the whole run's");
        return $"{ranges.Count}: {(same ? "the whole run's" : "DIFFER")}";
    }

    /// <summary>
    /// Whether <paramref name="value"/> is what FFmpeg printed as <paramref name="text"/> (%g, six significant digits;
    /// times to as many): within half a unit of its last digit.
    /// </summary>
    static bool Printed(string text, double value)
    {
        int e = text.IndexOfAny(['e', 'E']);
        string mantissa = e < 0 ? text : text[..e];
        int exponent = e < 0 ? 0 : int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        int dot = mantissa.IndexOf('.');
        int decimals = dot < 0 ? 0 : mantissa.Length - dot - 1;
        double unit = Math.Pow(10, exponent - decimals);
        return Math.Abs(double.Parse(text, CultureInfo.InvariantCulture) - value) <= unit / 2 * (1 + 1e-9);
    }

    sealed record Span(string Start, string End);

    sealed record FfmpegPicture(List<Dictionary<string, string>> Frames, string? Crop, List<Span> Black0, List<Span> Black2);

    /// <summary>
    /// One pass of FFmpeg 8.1 (the ffmpeg beside the libraries) over the video with every filter: signalstats and the
    /// per-frame cropdetect printed by metadata=print, blackdetect at d=0 and at its default, cropdetect with its defaults
    /// in the log. -copyts keeps the stream's own timestamps. -reinit_filter 0 keeps one graph, configured from the first
    /// frame, when a frame's tags change (tags-change-*.mp4): ffmpeg would otherwise build the graph again there, which
    /// starts the metadata file and the filters' state over, and the meter measures every frame with the video's range.
    /// </summary>
    static FfmpegPicture Ffmpeg(string libs, string sample)
    {
        string metadata = Path.GetTempFileName();
        try
        {
            string graph = $"signalstats=stat=brng,cropdetect=reset=1:skip=0,metadata=mode=print:file={metadata}," +
                "blackdetect=d=0,blackdetect,cropdetect";
            string log = Run(Path.Combine(libs, "ffmpeg"), "-hide_banner", "-nostats", "-nostdin", "-copyts", "-reinit_filter", "0", "-i", sample,
                "-map", "0:v:0", "-vf", graph, "-f", "null", "-");

            var frames = new List<Dictionary<string, string>>();
            foreach (string line in File.ReadLines(metadata))
            {
                if (line.StartsWith("frame:", StringComparison.Ordinal))
                    frames.Add([]);
                else if (line.IndexOf('=') is int eq and > 0 && frames.Count > 0)
                    frames[^1][line[..eq]] = line[(eq + 1)..];
            }

            string? crop = null;
            var black = new Dictionary<string, List<Span>>();
            foreach (string line in log.Split('\n'))
            {
                // [Parsed_blackdetect_3 @ 0x...] black_start:1.96 black_end:2.52 black_duration:0.56
                // [Parsed_cropdetect_5 @ 0x...] x1:0 x2:351 ... crop=352:288:0:0
                if (line.Contains("[Parsed_cropdetect_5 ", StringComparison.Ordinal))
                    crop = line[line.LastIndexOf("crop=", StringComparison.Ordinal)..].Trim();
                int at = line.IndexOf("[Parsed_blackdetect_", StringComparison.Ordinal);
                if (at < 0 || !line.Contains("black_start:", StringComparison.Ordinal))
                    continue;
                string filter = line[(at + 1)..line.IndexOf(' ', at)];
                var words = line[(line.IndexOf(']', at) + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Split(':')).ToDictionary(w => w[0], w => w[1]);
                if (!black.TryGetValue(filter, out var list))
                    black[filter] = list = [];
                list.Add(new Span(words["black_start"], words["black_end"]));
            }

            return new FfmpegPicture(frames, crop, black.GetValueOrDefault("Parsed_blackdetect_3") ?? [],
                black.GetValueOrDefault("Parsed_blackdetect_4") ?? []);
        }
        finally
        {
            File.Delete(metadata);
        }
    }

    static string Run(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args)
            start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{program} failed ({process.ExitCode}): {stderr}");
        return stderr + stdout.Result;
    }
}
