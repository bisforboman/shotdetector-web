// The native exactness check (README, "Checks"): for each sample, ShotDetectorWeb.Core's description of the video
// (YuvVideo) against FFmpeg's (FrameReader's probe and first decoded frame, as ShotDetector's FrameInputTests builds it),
// then every detector run on (a) the core library (ManagedCodecs + FastYuv) and (b) ShotDetector's Detect(path) with
// FFmpeg in-process and the same converter: frame count, frame rate, cuts, scene CSV, stats CSV, the shots as JSON
// and HTML (Shots.Json, Shots.Html: the page's other downloads) and the editors' timeline files (TimelineFiles: EDL,
// FCPXML, FCP7 XML, OTIO, QP, Aegisub keyframes, each given the sample's file name) must be equal byte for byte, and the
// YouTube chapters (TimelineFiles.Chapters at 10 and 30 s, Timeline's) too, within YouTube's rules (ChaptersCheck.cs). Writes (a)'s files to --out
// (the wasm check compares its own with them), (b)'s next to them where they differ.
// Then each sample's video packets against ffprobe's (PacketCheck.cs), whose lines go to --out for the wasm check, its
// frames' types against ffprobe's pict_type (FrameTypeCheck.cs), whole, on four threads and in ranges, ffprobe's going
// to --out too, and its audio track's description (AudioStreamCheck.cs). And every sample's run with every detector split into ranges at
// keyframes, scored on their own and merged, against the whole run (RangeCheck.cs), and decoded on four threads against
// one (ThreadsCheck.cs); stopped twice and resumed, on one range and split, against the whole run (ResumeCheck.cs).
// And the page's defaults, read from its app.js, against ShotDetector's own (DefaultsCheck.cs).
// And the picture's measures against FFmpeg's blackdetect, signalstats and cropdetect, and the colour bars by their
// definition (PictureCheck.cs).
//   NativeCheck [--out dir] [--reject file...] sample.mp4...    (SHOTDETECTOR_FFMPEG_LIBS: FFmpeg 8.1's shared libraries)
// Exit code 0 when everything agreed and every --reject file was refused with an UnsupportedVideoException, when opened
// or by its run.
using System.Globalization;
using ShotDetector;
using ShotDetector.FastYuv;
using ShotDetectorWeb;
using ShotDetectorWeb.Checks;

string libs = Environment.GetEnvironmentVariable("SHOTDETECTOR_FFMPEG_LIBS")
    ?? throw new InvalidOperationException("Set SHOTDETECTOR_FFMPEG_LIBS to an FFmpeg 8.1 shared build's folder.");
string outDir = "out/native";
var samples = new List<string>();
var rejects = new List<string>();
// What the refusal of each codec and container sample must name (tools/make-samples.sh; app-check.mjs keeps the same).
var RefusalNames = new Dictionary<string, string>
{
    ["hevc.mp4"] = "FFmpeg's hevc",
    ["vp8.webm"] = "FFmpeg's vp8",
    ["h264.mkv"] = "H.264 in Matroska",
    ["mpeg4.mp4"] = "FFmpeg's mpeg4",
    ["h264.avi"] = "FFmpeg's h264",
};
var list = samples;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out")
        outDir = args[++i];
    else if (args[i] == "--reject")
        list = rejects;
    else if (args[i] == "--samples")
        list = samples;
    else
        list.Add(args[i]);
}

Directory.CreateDirectory(outDir);
FrameReader.FFmpegLibraries.Load(libs);
int failures = 0;

Console.WriteLine("| Sample | Run | Frames | Shots | YuvVideo | Scenes CSV | Stats CSV | Shots JSON, HTML | Timelines, chapters | Core time | FFmpeg time |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
foreach (string sample in samples)
{
    VideoInput input;
    try
    {
        input = FfmpegDecoder.Open(sample);
    }
    catch (Exception e)
    {
        Console.WriteLine($"| {Path.GetFileName(sample)} | - | - | - | open failed: {e.GetType().Name}: {e.Message} | | | | | | |");
        failures++;
        continue;
    }

    var differences = Differences(input.Video, Oracle(sample));
    // The damaged frames (DamagedCheck.cs): FFmpeg's, from its MJPEG decoder's log, for an AVI.
    IReadOnlyList<int>? ffmpegDamaged = null;
    string described = differences.Count == 0 ? "same" : "DIFFERS: " + string.Join("; ", differences);
    if (differences.Count > 0)
        failures++;

    foreach (var (run, baseOptions) in Runs.All)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ours = input.Detect(baseOptions);
        var oursTime = watch.Elapsed;
        watch.Restart();
        // ShotDetector 3.28.0 runs FFmpeg's yadif over 9 to 16 bits on one thread, as its C code: the same on every run,
        // FastYuv's Yadif's (before it, NativeCheck turned FFmpeg's SIMD off for such a run).
        var file = ShotDetection.Detect(sample, baseOptions with { FfmpegDirectory = libs, Decoder = VideoDecoder.InProcess, Yuv420Converter = new SwscaleYuv420() });

        var fileTime = watch.Elapsed;

        var (scenes, stats) = Runs.Csvs(ours);
        var (fileScenes, fileStats) = Runs.Csvs(file);
        // A damaged frame's rows can't equal FFmpeg's (decision 2026-11-02-damaged-jpeg.md): the core's damaged frames
        // must be FFmpeg's, and the stats the FFmpeg path's around them.
        var damaged = input.DamagedFrames(ours.FrameCount);
        int window = baseOptions.FrameWindow;
        bool deinterlaced = ours.Video.Deinterlaced;
        bool statsSame = DamagedCheck.Outside(stats, damaged, window, deinterlaced) == DamagedCheck.Outside(fileStats, damaged, window, deinterlaced);
        string name = Path.Combine(outDir, Runs.BaseName(sample, run));
        File.WriteAllText(name + ".scenes.csv", scenes);
        File.WriteAllText(name + ".stats.csv", stats);
        var (json, html) = Runs.ShotFiles(ours);
        var (fileJson, fileHtml) = Runs.ShotFiles(file);
        File.WriteAllText(name + ".shots.json", json);
        File.WriteAllText(name + ".shots.html", html);
        // The editors' files, both results given the sample's name (the page gives the picked file's).
        var timelinesDiffer = new List<string>();
        foreach (var (format, extension) in TimelineFiles.Formats)
        {
            string timeline = TimelineFiles.Write(ours, format, Path.GetFileName(sample));
            string fileTimeline = TimelineFiles.Write(file, format, Path.GetFileName(sample));
            File.WriteAllText($"{name}.{extension}", timeline);
            if (timeline != fileTimeline)
            {
                timelinesDiffer.Add(format);
                File.WriteAllText($"{name}.ffmpeg.{extension}", fileTimeline);
            }
        }

        // The YouTube chapters (Timeline's) at YouTube's minimum and the page's default: the FFmpeg path's,
        // and within YouTube's rules.
        foreach (int minimum in ChaptersCheck.Minimums)
        {
            string chaptersJson = TimelineFiles.Chapters(ours, minimum), fileChaptersJson = TimelineFiles.Chapters(file, minimum);
            File.WriteAllText($"{name}.chapters-{minimum}.json", chaptersJson);
            var broken = ChaptersCheck.Problems(ours, chaptersJson);
            if (chaptersJson != fileChaptersJson || broken.Count > 0)
            {
                timelinesDiffer.Add($"chapters {minimum} s{(broken.Count > 0 ? $" ({string.Join("; ", broken)})" : "")}");
                File.WriteAllText($"{name}.ffmpeg.chapters-{minimum}.json", fileChaptersJson);
            }
        }

        var problems = new List<string>();
        if (!file.Video.DecodedInProcess)
            problems.Add("the FFmpeg path didn't decode in-process");
        if (ours.FrameCount != file.FrameCount)
            problems.Add($"frames {ours.FrameCount} vs {file.FrameCount}");
        if (ours.Video.FrameRate != file.Video.FrameRate || ours.Video.OpenCvFrameCount != file.Video.OpenCvFrameCount)
            problems.Add($"rate/length {ours.Video.FrameRate}/{ours.Video.OpenCvFrameCount} vs {file.Video.FrameRate}/{file.Video.OpenCvFrameCount}");
        if (ours.Video.Duration != file.Video.Duration)
            problems.Add($"duration {ours.Video.Duration} vs {file.Video.Duration}");
        if ((ours.Video.ProcessedWidth, ours.Video.ProcessedHeight) != (file.Video.ProcessedWidth, file.Video.ProcessedHeight))
            problems.Add("processed size");
        if ((ours.Video.FieldOrder, ours.Video.Deinterlaced) != (file.Video.FieldOrder, file.Video.Deinterlaced))
            problems.Add($"field order, deinterlaced {ours.Video.FieldOrder} {ours.Video.Deinterlaced} vs {file.Video.FieldOrder} {file.Video.Deinterlaced}");
        if ((ours.Video.Width, ours.Video.Height, ours.Video.Rotation) != (file.Video.Width, file.Video.Height, file.Video.Rotation))
            problems.Add($"size and rotation {ours.Video.Width}x{ours.Video.Height} {ours.Video.Rotation} vs {file.Video.Width}x{file.Video.Height} {file.Video.Rotation}");
        if (!ours.Cuts.SequenceEqual(file.Cuts))
            problems.Add("cuts");
        if (damaged.Count > 0 || sample.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
        {
            ffmpegDamaged ??= DamagedCheck.Ffmpeg(libs, sample);
            if (!damaged.SequenceEqual(ffmpegDamaged))
                problems.Add($"damaged frames [{string.Join(", ", damaged)}], FFmpeg's [{string.Join(", ", ffmpegDamaged)}]");
        }
        string scenesResult = scenes == fileScenes ? "same" : "DIFFERS";
        string statsResult = stats == fileStats ? $"same ({stats.Length:N0} bytes)"
            : statsSame ? $"same but the {DamagedCheck.Named(damaged, window, deinterlaced)} (damaged)" : "DIFFERS";
        string shotsResult = (json == fileJson ? "same" : "JSON DIFFERS") + ", " + (html == fileHtml ? "same" : "HTML DIFFERS");
        string timelinesResult = timelinesDiffer.Count == 0 ? "same" : $"DIFFERS: {string.Join(", ", timelinesDiffer)}";
        if (timelinesDiffer.Count > 0)
            failures++;
        if (scenes != fileScenes || !statsSame || json != fileJson || html != fileHtml || problems.Count > 0)
        {
            failures++;
            File.WriteAllText(name + ".ffmpeg.scenes.csv", fileScenes);
            File.WriteAllText(name + ".ffmpeg.stats.csv", fileStats);
            File.WriteAllText(name + ".ffmpeg.shots.json", fileJson);
            File.WriteAllText(name + ".ffmpeg.shots.html", fileHtml);
        }

        string frames = problems.Count == 0 ? $"{ours.FrameCount}" : $"{ours.FrameCount} ({string.Join(", ", problems)})";
        Console.WriteLine($"| {Path.GetFileName(sample)} | {run} | {frames} | {ours.Shots.Count} | {described} | {scenesResult} | {statsResult} | {shotsResult} | {timelinesResult} | {oursTime.TotalSeconds:F2} s | {fileTime.TotalSeconds:F2} s |");
        described = "";
    }
}

failures += PacketCheck.Run(samples, libs, outDir);
failures += FrameTypeCheck.Run(samples, libs, outDir);
failures += FieldCheck.Run(samples, libs);
failures += RangeCheck.Run(samples, outDir);
failures += ThreadsCheck.Run(samples, outDir);
failures += ResumeCheck.Run(samples, outDir);
failures += CorrectionCheck.Run(samples, libs, outDir);

foreach (string reject in rejects)
{
    try
    {
        // Refused when opened, or, for what only the frames tell (a size that changes midway), by the run: the
        // core's frames refuse it before ShotDetector would read one at the video's size.
        FfmpegDecoder.Open(reject).Detect();
        Console.WriteLine($"Not refused: {reject}");
        failures++;
    }
    catch (UnsupportedVideoException e)
    {
        Console.WriteLine($"Refused {Path.GetFileName(reject)}: {e.Message}");
        // A codec or container the core doesn't read is named as FFmpeg names it (backlog "A refusal that says what to
        // do"); the page's own words about what to do instead are page-check's.
        if (RefusalNames.TryGetValue(Path.GetFileName(reject), out string? named) && !e.Message.Contains(named, StringComparison.Ordinal))
        {
            Console.WriteLine($"  Its message doesn't name {named}.");
            failures++;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Refused {Path.GetFileName(reject)} with {e.GetType().Name}, not UnsupportedVideoException: {e.Message}");
        failures++;
    }
}

failures += AudioCheck.Run(samples, libs, outDir);
failures += PictureCheck.Run(samples, libs, outDir);
failures += AudioStreamCheck.Run(samples);
failures += DefaultsCheck.Run(samples);

Console.WriteLine(failures == 0 ? "All agree." : $"{failures} differences or failures.");
return failures == 0 ? 0 : 1;

// The video as ShotDetector's FrameInputTests describes a file with FFmpeg: the probe's stream, the first decoded frame's
// colour tags, the format's duration (as the file path's DetectedVideo.Duration has it).
static YuvVideo Oracle(string path)
{
    var probe = FrameReader.MediaProbe.Probe(path);
    var info = probe.Video!;
    string colorSpace, colorRange;
    string? chromaLocation;
    // Every frame FFmpeg decodes, its pts: the frames' labels, without those an edit list leaves out (ShotDetector's
    // YuvVideo.FrameTimestamps, which the core gives at every rate).
    var timestamps = new List<long>();
    using (var decoder = new FrameReader.FrameDecoder(path, new FrameReader.FrameDecoderOptions { Autorotate = false }))
    {
        if (!decoder.Next())
            throw new InvalidOperationException($"FFmpeg decodes no frame of {path}.");
        colorSpace = decoder.ColorSpace ?? info.ColorSpace ?? "";
        colorRange = decoder.ColorRange ?? info.ColorRange ?? "";
        // The decoder's, as the file path reads it (the first frame's; the stream's can be the container's).
        chromaLocation = decoder.ChromaLocation;
        do
            timestamps.Add(decoder.FramePts ?? long.MinValue);
        while (decoder.Next());
    }

    return new YuvVideo
    {
        Width = info.Width,
        Height = info.Height,
        // avg_frame_rate, or r_frame_rate where FFmpeg has no average (a variable-rate WebM without a DefaultDuration),
        // as the file path reports it (ShotDetector 3.28.0's Detect(path): decision 2026-11-05-webm-without-default-duration.md).
        FrameRate = info.AverageFrameRate.Num > 0 && info.AverageFrameRate.Den > 0 ? info.AverageFrameRate : info.FrameRate,
        PixelFormat = info.PixelFormat!,
        ColorSpace = colorSpace,
        FullRange = colorRange == "pc",
        ChromaLocation = chromaLocation,
        TimeBase = info.TimeBase,
        StartPts = info.StartPts ?? 0,
        FrameCount = info.FrameCount,
        // As the file path takes the display matrix's angle (VideoReader): rounded, its remainder by 360.
        Rotation = info.Rotation is { } r ? (int)Math.Round(r) % 360 : 0,
        // As the file path reads it: ffprobe's "duration" (the format's, printed with six decimals), to the nearest tick.
        Duration = probe.Duration is { } d
            ? TimeSpan.FromTicks((long)Math.Round(double.Parse((d.Ticks / 10 * (1 / 1_000_000.0)).ToString("F6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond))
            : null,
        FieldOrder = info.FieldOrder,
        FrameTimestamps = [.. timestamps.Order()],
        Codec = info.Codec,
        Container = probe.Container,
    };
}

static List<string> Differences(YuvVideo ours, YuvVideo ffmpeg)
{
    var d = new List<string>();
    void Check<T>(string name, T a, T b)
    {
        if (!EqualityComparer<T>.Default.Equals(a, b))
            d.Add($"{name} {a} vs FFmpeg's {b}");
    }

    Check("size", (ours.Width, ours.Height), (ffmpeg.Width, ffmpeg.Height));
    Check("frame rate", ours.FrameRate.Num / (double)ours.FrameRate.Den, ffmpeg.FrameRate.Num / (double)ffmpeg.FrameRate.Den);
    Check("pixel format", ours.PixelFormat, ffmpeg.PixelFormat);
    Check("colour space", ours.ColorSpace, ffmpeg.ColorSpace);
    Check("full range", ours.FullRange, ffmpeg.FullRange);
    Check("chroma location", ours.ChromaLocation, ffmpeg.ChromaLocation);
    Check("time base", ours.TimeBase, ffmpeg.TimeBase);
    Check("start pts", ours.StartPts, ffmpeg.StartPts);
    Check("frame count", ours.FrameCount, ffmpeg.FrameCount);
    Check("duration", ours.Duration, ffmpeg.Duration);
    Check("rotation", ours.Rotation, ffmpeg.Rotation);
    Check("field order", ours.FieldOrder, ffmpeg.FieldOrder);
    // Every frame's pts, at any rate (the core gives them always, so that a stopped run ends where the file path's
    // does: ShotDetector 3.29.0): FFmpeg's decoded frames' pts, sorted.
    if (ours.FrameTimestamps is not { } given)
        d.Add("frame timestamps: none given");
    else if (ffmpeg.FrameTimestamps is not { } probed || !given.SequenceEqual(probed))
        d.Add($"frame timestamps: {given.Count} given, not FFmpeg's {ffmpeg.FrameTimestamps?.Count} frames' pts");

    Check("codec", ours.Codec, ffmpeg.Codec);
    Check("container", ours.Container, ffmpeg.Container);
    return d;
}
