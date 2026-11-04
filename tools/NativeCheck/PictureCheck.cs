// The picture check (README, "Checks"; decision 2026-10-30-picture-measures.md): for each sample, the picture measures
// of the default run (PictureMeter, alongside detection) against FFmpeg's filters on the same file, exactly: the frames
// are FFmpeg's (the CSVs prove it) and the filters count integers over them.
// The 10-bit samples as the others: FFmpeg's filters take yuv420p10le as it is and count 10-bit code values (their
// thresholds by the depth), as PictureMeter does.
// - signalstats=stat=brng per frame: YMIN and YMAX equal, YAVG and BRNG to the digits FFmpeg prints (%g);
// - cropdetect=reset=1:skip=0 per frame: x1, x2, y1, y2, w, h, x, y equal; and cropdetect with its defaults (reset=0,
//   skip=2, round=16) after the last frame, its crop equal;
// - blackdetect with d=0 and with its default 2 s: ShotDetector's black spans (BlackSpans, decision
//   2026-11-04-black-spans-and-times.md), the 2 s ones those the summary marks long, their times to the digits FFmpeg
//   prints (without -copyts: from the video stream's start, as ShotDetector times them);
// - the colour bars, which no FFmpeg filter finds, by their definition: on the samples whose last shot is smptehdbars
//   (make-samples.sh's three(), the interlaced ones woven, the rotated ones upright, the
//   variable-rate ones at their own timestamps, the 10-bit ones in 10-bit levels), one span, the last shot's frames (from the default run's last cut; decoded frame 100 on for
//   the variable-rate ones, whose cuts are numbered by time at the average rate); on the fades sample
//   (smptebars fading in for 0.3 s), one span to the end, starting within the fade; on the others, none;
// - freezedetect with its defaults: ShotDetector's still pictures (FreezeSpans, decision 2026-11-03-spans-on-ranges.md),
//   their freeze_start and freeze_end to the digits FFmpeg prints (from the stream's start too; one still at the end has
//   no freeze_end); and a run deinterlacing (Deinterlace On), whose still pictures and black spans are of its
//   deinterlaced frames, against the FFmpeg path's (Detect(path): FFmpeg's yadif), frames and times.
// And the run split into ranges as the page splits it (at most 4), each measured by its own meter while it is scored:
// joined, their measures must be the whole run's, byte for byte; merged after ShotDetector's FirstRangeToScoreAgain
// (VideoInput.ScoreAgain: a range that starts inside a still scored again), their still pictures and black spans the
// whole run's; and the run stopped half-way and resumed (VideoInput.Continue: a rest inside a still given from its first
// frame), its still pictures and black spans the whole run's too. Each sample's summary (the App's Picture() JSON) goes to the output folder as
// <sample>.picture.json, which the wasm check compares its own with.
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
        Console.WriteLine("| Sample | Frames | signalstats (per frame) | cropdetect (per frame; whole) | blackdetect (d=0; 2 s) | Colour bars | freezedetect | Ranges | Core time |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
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
            while (cells.Count < 8)
                cells.Add("");
            if (problems.Count > 0)
                cells[0] += " **DIFFERS: " + string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? $"; {problems.Count - 5} more" : "") + "**";
            Console.WriteLine($"| {Path.GetFileName(sample)} | {string.Join(" | ", cells)} |");
        }

        return failures;
    }

    static void Check(string sample, string libs, string outDir, List<string> problems, List<string> cells)
    {
        var input = FfmpegDecoder.Open(sample);
        var meter = new PictureMeter(input.Video);
        var watch = Stopwatch.StartNew();
        var result = input.Detect(Spans, picture: meter);
        var time = watch.Elapsed;
        var ours = meter.Measures;
        if (ours.Count != result.FrameCount)
            problems.Add($"{ours.Count} frames measured, the run read {result.FrameCount}");
        cells.Add($"{ours.Count}");

        var theirs = Ffmpeg(libs, sample);
        if (theirs.Frames.Count != ours.Count)
            problems.Add($"FFmpeg's filters saw {theirs.Frames.Count} frames");

        // signalstats and cropdetect, frame by frame; a damaged frame's left out (decision 2026-11-02-damaged-jpeg.md:
        // FFmpeg's picture there is a frame from its buffer pool, DamagedCheck.cs).
        var damaged = input.DamagedFrames(result.FrameCount);
        int stats = 0, crops = 0;
        for (int i = 0; i < Math.Min(ours.Count, theirs.Frames.Count); i++)
        {
            if (damaged.Contains(i))
                continue;
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
        string left = damaged.Count == 0 ? "" : $" (damaged frame{(damaged.Count == 1 ? "" : "s")} {string.Join(", ", damaged)} left out)";
        cells.Add((stats == 0 ? "same" : $"{stats} frames differ") + left);

        var whole = ours.Crop();
        if (whole?.ToString() != theirs.Crop)
            problems.Add($"cropdetect's crop {whole?.ToString() ?? "none"}, FFmpeg's {theirs.Crop ?? "none"}");
        cells.Add($"{(crops == 0 ? "same" : $"{crops} frames differ")}; {whole?.ToString() ?? "none"}");

        // blackdetect at d=0 and its default 2 s: ShotDetector's spans, and those the summary marks long.
        if (result.BlackSpans is null)
            problems.Add("the run didn't look for black spans");
        var black = PictureSummary.BlackOf(result, ours, input.Video);
        var blackCells = new List<string>();
        foreach (var (d, theirSpans) in new[] { (0.0, theirs.Black0), (2.0, theirs.Black2) })
        {
            var spans = black.Where(s => d == 0 || s.Long).ToList();
            bool same = spans.Count == theirSpans.Count && spans.Zip(theirSpans).All(p =>
                Printed(p.Second.Start, p.First.Start) && Printed(p.Second.End, p.First.End));
            if (!same)
            {
                problems.Add($"blackdetect d={d}: {string.Join(", ", spans.Select(s => $"{s.Start:0.######}-{s.End:0.######}"))}, " +
                    $"FFmpeg's {string.Join(", ", theirSpans.Select(s => $"{s.Start}-{s.End}"))}");
            }

            blackCells.Add(spans.Count == 0 ? "none" : string.Join(", ", spans.Select(s => $"{s.Start:0.###}-{s.End:0.###} s")));
        }

        cells.Add(string.Join("; ", blackCells));
        cells.Add(Bars(sample, ours, result, problems));
        var freeze = PictureFreeze.Of(result);
        cells.Add(Freeze(freeze, theirs.Freeze, problems) + "; " + Deinterlaced(sample, input, libs, problems));
        cells.Add(Ranges(input, ours, result, problems));
        cells.Add($"{time.TotalSeconds:F2} s");

        File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(sample) + ".picture.json"),
            JsonSerializer.Serialize(PictureSummary.Of(ours, black, freeze), PictureJson.Default.PictureSummary));
    }

    /// <summary>
    /// The page's run with the still pictures and black spans looked for (the App's options: freezedetect's defaults,
    /// blackdetect with <c>d=0</c>).
    /// </summary>
    static DetectionOptions Spans => new() { FreezeDetect = new FreezeDetectOptions(), BlackDetect = new BlackDetectOptions { MinDuration = TimeSpan.Zero } };

    /// <summary>
    /// The still pictures against freezedetect's, both from the stream's start (ffmpeg without -copyts; ShotDetector's).
    /// </summary>
    static string Freeze(PictureFreeze ours, List<FreezeLine> theirs, List<string> problems)
    {
        var spans = ours.Spans!;
        bool same = spans.Count == theirs.Count && spans.Zip(theirs).All(p => Printed(p.Second.Start, p.First.Start) &&
            (p.First.End is { } end ? p.Second.End is { } e && Printed(e, end) : p.Second.End is null));
        string Shown(PictureStill s) => $"{s.Start:0.######}-{(s.End is { } e ? e.ToString("0.######", CultureInfo.InvariantCulture) : "end")}";
        if (!same)
            problems.Add($"freezedetect: {string.Join(", ", spans.Select(Shown))}, FFmpeg's {string.Join(", ", theirs.Select(t => $"{t.Start}-{t.End ?? "end"}"))}");
        return spans.Count == 0 ? "none" : string.Join(", ", spans.Select(s =>
            $"{s.Start:0.###}-{(s.End is { } e ? e.ToString("0.###", CultureInfo.InvariantCulture) : "end")} s"));
    }

    /// <summary>
    /// A run deinterlacing every frame (Deinterlace On): its still pictures and black spans, of the deinterlaced frames,
    /// against the FFmpeg path's (FFmpeg's yadif).
    /// </summary>
    static string Deinterlaced(string sample, VideoInput input, string libs, List<string> problems)
    {
        var options = Spans with { Deinterlace = DeinterlaceMode.On };
        var ours = input.Detect(options);
        var file = ShotDetection.Detect(sample, options with { FfmpegDirectory = libs, Decoder = VideoDecoder.InProcess, Yuv420Converter = new ShotDetector.FastYuv.SwscaleYuv420() });
        if (!SameStills(ours.FreezeSpans, file.FreezeSpans))
        {
            problems.Add($"deinterlaced, still pictures {Shown(ours.FreezeSpans)}, the FFmpeg path's {Shown(file.FreezeSpans)}");
            return "deinterlaced: DIFFER";
        }

        if (!SameBlack(ours.BlackSpans, file.BlackSpans))
        {
            problems.Add($"deinterlaced, black spans {ShownBlack(ours.BlackSpans)}, the FFmpeg path's {ShownBlack(file.BlackSpans)}");
            return "deinterlaced: black DIFFERS";
        }

        return $"deinterlaced: {(ours.FreezeSpans!.Count == 0 ? "none" : Shown(ours.FreezeSpans))}, the FFmpeg path's";
    }

    /// <summary>Whether two runs' still pictures are the same: frames and times.</summary>
    static bool SameStills(IReadOnlyList<FreezeSpan>? a, IReadOnlyList<FreezeSpan>? b) =>
        a is not null && b is not null && a.Count == b.Count && a.Zip(b).All(p => p.First.StartFrame == p.Second.StartFrame &&
            p.First.EndFrame == p.Second.EndFrame && p.First.Start == p.Second.Start && p.First.End == p.Second.End);

    /// <summary>Whether two runs' black spans are the same: frames and times.</summary>
    static bool SameBlack(IReadOnlyList<BlackSpan>? a, IReadOnlyList<BlackSpan>? b) =>
        a is not null && b is not null && a.Count == b.Count && a.Zip(b).All(p => p.First.StartFrame == p.Second.StartFrame &&
            p.First.EndFrame == p.Second.EndFrame && p.First.Start == p.Second.Start && p.First.End == p.Second.End);

    /// <summary>Black spans as frames.</summary>
    static string ShownBlack(IReadOnlyList<BlackSpan>? spans) => spans is null ? "not looked for" :
        string.Join(", ", spans.Select(s => $"{s.StartFrame}-{s.EndFrame}"));

    /// <summary>Still pictures as frames.</summary>
    static string Shown(IReadOnlyList<FreezeSpan>? spans) => spans is null ? "not looked for" :
        string.Join(", ", spans.Select(s => $"{s.StartFrame}-{s.EndFrame?.ToString(CultureInfo.InvariantCulture) ?? "end"}"));

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
            name.StartsWith("yuv420p10-", StringComparison.Ordinal) || name.StartsWith("odd-height-", StringComparison.Ordinal) ||
            name.StartsWith("vp9-", StringComparison.Ordinal) || name.StartsWith("av1-", StringComparison.Ordinal);
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

    /// <summary>
    /// The run split into ranges, each measured while it is scored, joined: the whole run's measures; merged after
    /// <see cref="VideoInput.ScoreAgain"/>, the whole run's still pictures; and stopped half-way and resumed
    /// (<see cref="VideoInput.Continue"/>), the whole run's still pictures too.
    /// </summary>
    static string Ranges(VideoInput input, PictureMeasures whole, DetectionResult run, List<string> problems)
    {
        var options = Spans;
        var ranges = input.Ranges(options, MostRanges);
        var parts = new List<PictureMeasures>();
        var scores = new List<RangeScores>();
        foreach (var range in ranges)
        {
            var meter = new PictureMeter(input.Video, range);
            scores.Add(input.ScoreRangeAsync(range, 8, _ => ValueTask.FromResult(true), options, picture: meter)
                .GetAwaiter().GetResult());
            parts.Add(PictureMeasures.FromBytes(meter.Measures.ToBytes()));
        }

        var joined = PictureMeasures.Join(parts);
        bool same = joined is not null && joined.ToBytes().AsSpan().SequenceEqual(whole.ToBytes());
        if (!same)
            problems.Add($"the {ranges.Count} ranges' measures joined ({joined?.Count ?? 0} frames) aren't the whole run's");

        var again = input.ScoreAgain(scores, options);
        int scoredAgain = again.Where((r, i) => !ReferenceEquals(r, scores[i])).Count();
        var merged = input.Merge(again, options);
        bool stills = SameStills(merged.FreezeSpans, run.FreezeSpans) && SameBlack(merged.BlackSpans, run.BlackSpans);
        if (!stills)
            problems.Add($"the {ranges.Count} ranges' still pictures {Shown(merged.FreezeSpans)}, black {ShownBlack(merged.BlackSpans)}, " +
                $"the whole run's {Shown(run.FreezeSpans)}, {ShownBlack(run.BlackSpans)}");

        // Stopped half-way and resumed, as the page's Resume goes on.
        int half = run.FrameCount / 2;
        var stopped = input.ScoreRangeAsync(input.Ranges(options, 1)[0], 1, given => ValueTask.FromResult(given < half), options)
            .GetAwaiter().GetResult();
        var go = input.Continue([stopped], options);
        var all = go.Earlier.ToList();
        if (go.Rest is { } rest)
            all.Add(input.ScoreRangeAsync(go.Earlier, rest, 8, _ => ValueTask.FromResult(true), options).GetAwaiter().GetResult());
        var resumed = input.Merge(input.ScoreAgain(input.FromStart(all), options), options);
        bool resumedStills = SameStills(resumed.FreezeSpans, run.FreezeSpans) && SameBlack(resumed.BlackSpans, run.BlackSpans);
        if (!resumedStills)
            problems.Add($"resumed at frame {half}, still pictures {Shown(resumed.FreezeSpans)}, black {ShownBlack(resumed.BlackSpans)}, " +
                $"the whole run's {Shown(run.FreezeSpans)}, {ShownBlack(run.BlackSpans)}");

        return $"{ranges.Count}: measures {(same ? "the whole run's" : "DIFFER")}, still pictures and black " +
            $"{(stills ? "the whole run's" : "DIFFER")}{(scoredAgain > 0 ? $" ({scoredAgain} scored again)" : "")}; " +
            $"resumed{(go.Rest is { Skip: > 0 } r ? $" from the still's first frame, {r.From}" : "")}: {(resumedStills ? "the whole run's" : "DIFFER")}";
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

    sealed record FreezeLine(string Start, string? End);

    sealed record FfmpegPicture(List<Dictionary<string, string>> Frames, string? Crop, List<Span> Black0, List<Span> Black2,
        List<FreezeLine> Freeze);

    /// <summary>
    /// Two passes of FFmpeg 8.1 (the ffmpeg beside the libraries) over the video: signalstats and the per-frame cropdetect
    /// printed by metadata=print and cropdetect with its defaults in the log, -copyts keeping the stream's own timestamps;
    /// then blackdetect at d=0 and at its default and freezedetect, without -copyts (their times from the stream's start,
    /// as ShotDetector's). -reinit_filter 0 keeps one graph, configured from the first
    /// frame, when a frame's tags change (tags-change-*.mp4): ffmpeg would otherwise build the graph again there, which
    /// starts the metadata file and the filters' state over, and the meter measures every frame with the video's range.
    /// </summary>
    // -apply_cropping codec: the decoded frames as the stream crops them (the SPS's frame cropping: a 320x240 coded as
    // 320x256), as ShotDetector reads them and the core measures them, without a container's clean aperture (the owner's
    // choice, 2026-11-04): FFmpeg 8.1's command line otherwise crops to that too ('clap': an iPhone's 872x653 in 960x720,
    // realworld.sh's phone-iphone-2018.mov), which neither detection path does. 'none' would drop the SPS's cropping too.
    static FfmpegPicture Ffmpeg(string libs, string sample)
    {
        string metadata = Path.GetTempFileName();
        try
        {
            string graph = $"signalstats=stat=brng,cropdetect=reset=1:skip=0,metadata=mode=print:file={metadata},cropdetect";
            string log = Run(Path.Combine(libs, "ffmpeg"), "-hide_banner", "-nostats", "-nostdin", "-copyts", "-reinit_filter", "0", "-apply_cropping", "codec", "-i", sample,
                "-map", "0:v:0", "-vf", graph, "-f", "null", "-");
            // The spans without -copyts: ffmpeg's times from the video stream's start, as ShotDetector's (a fragmented
            // stream starting at 0.08 s: its still at 5.6 s, not 5.68).
            string spansLog = Run(Path.Combine(libs, "ffmpeg"), "-hide_banner", "-nostats", "-nostdin", "-reinit_filter", "0", "-apply_cropping", "codec", "-i", sample,
                "-map", "0:v:0", "-vf", "blackdetect=d=0,blackdetect,freezedetect", "-f", "null", "-");

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
            var freeze = new List<FreezeLine>();
            foreach (string line in log.Split('\n'))
            {
                // [Parsed_cropdetect_3 @ 0x...] x1:0 x2:351 ... crop=352:288:0:0
                if (line.Contains("[Parsed_cropdetect_3 ", StringComparison.Ordinal))
                    crop = line[line.LastIndexOf("crop=", StringComparison.Ordinal)..].Trim();
            }

            foreach (string line in spansLog.Split('\n'))
            {
                // [Parsed_freezedetect_2 @ 0x...] lavfi.freezedetect.freeze_start: 48.4583
                // [Parsed_freezedetect_2 @ 0x...] lavfi.freezedetect.freeze_end: 50.8333
                foreach (var (key, isEnd) in new[] { ("lavfi.freezedetect.freeze_start: ", false), ("lavfi.freezedetect.freeze_end: ", true) })
                {
                    int k = line.IndexOf(key, StringComparison.Ordinal);
                    if (k < 0)
                        continue;
                    string value = line[(k + key.Length)..].Trim();
                    if (!isEnd)
                        freeze.Add(new FreezeLine(value, null));
                    else if (freeze.Count > 0)
                        freeze[^1] = freeze[^1] with { End = value };
                }

                // [Parsed_blackdetect_0 @ 0x...] black_start:1.96 black_end:2.52 black_duration:0.56
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

            return new FfmpegPicture(frames, crop, black.GetValueOrDefault("Parsed_blackdetect_0") ?? [],
                black.GetValueOrDefault("Parsed_blackdetect_1") ?? [], freeze);
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
