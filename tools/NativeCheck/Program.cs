// The native exactness check (README, "Checks"): for each sample, ShotDetectorWeb.Core's description of the video
// (YuvVideo) against FFmpeg's (FrameReader's probe and first decoded frame, as ShotDetector's FrameInputTests builds it),
// then every detector run on (a) the core library (ManagedCodecs + FastYuv) and (b) ShotDetector's Detect(path) with
// FFmpeg in-process and the same converter: frame count, frame rate, cuts, scene CSV, stats CSV and the shots as JSON
// and HTML (Shots.Json, Shots.Html: the page's other downloads) must be equal byte for byte. Writes (a)'s files to --out
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
// Exit code 0 when everything agreed and every --reject file was refused with an UnsupportedVideoException.
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

Console.WriteLine("| Sample | Run | Frames | Shots | YuvVideo | Scenes CSV | Stats CSV | Shots JSON, HTML | Core time | FFmpeg time |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
foreach (string sample in samples)
{
    VideoInput input;
    try
    {
        input = VideoInput.Open(sample);
    }
    catch (Exception e)
    {
        Console.WriteLine($"| {Path.GetFileName(sample)} | - | - | - | open failed: {e.GetType().Name}: {e.Message} | | | | | |");
        failures++;
        continue;
    }

    var differences = Differences(input.Video, Oracle(sample));
    string described = differences.Count == 0 ? "same" : "DIFFERS: " + string.Join("; ", differences);
    if (differences.Count > 0)
        failures++;

    foreach (var (run, baseOptions) in Runs.All)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ours = input.Detect(baseOptions);
        var oursTime = watch.Elapsed;
        watch.Restart();
        var file = ShotDetection.Detect(sample, baseOptions with { FfmpegDirectory = libs, Decoder = VideoDecoder.InProcess, Yuv420Converter = new SwscaleYuv420() });
        var fileTime = watch.Elapsed;

        var (scenes, stats) = Runs.Csvs(ours);
        var (fileScenes, fileStats) = Runs.Csvs(file);
        string name = Path.Combine(outDir, Runs.BaseName(sample, run));
        File.WriteAllText(name + ".scenes.csv", scenes);
        File.WriteAllText(name + ".stats.csv", stats);
        var (json, html) = Runs.ShotFiles(ours);
        var (fileJson, fileHtml) = Runs.ShotFiles(file);
        File.WriteAllText(name + ".shots.json", json);
        File.WriteAllText(name + ".shots.html", html);

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
        if (!ours.Cuts.SequenceEqual(file.Cuts))
            problems.Add("cuts");
        string scenesResult = scenes == fileScenes ? "same" : "DIFFERS";
        string statsResult = stats == fileStats ? $"same ({stats.Length:N0} bytes)" : "DIFFERS";
        string shotsResult = (json == fileJson ? "same" : "JSON DIFFERS") + ", " + (html == fileHtml ? "same" : "HTML DIFFERS");
        if (scenes != fileScenes || stats != fileStats || json != fileJson || html != fileHtml || problems.Count > 0)
        {
            failures++;
            File.WriteAllText(name + ".ffmpeg.scenes.csv", fileScenes);
            File.WriteAllText(name + ".ffmpeg.stats.csv", fileStats);
            File.WriteAllText(name + ".ffmpeg.shots.json", fileJson);
            File.WriteAllText(name + ".ffmpeg.shots.html", fileHtml);
        }

        string frames = problems.Count == 0 ? $"{ours.FrameCount}" : $"{ours.FrameCount} ({string.Join(", ", problems)})";
        Console.WriteLine($"| {Path.GetFileName(sample)} | {run} | {frames} | {ours.Shots.Count} | {described} | {scenesResult} | {statsResult} | {shotsResult} | {oursTime.TotalSeconds:F2} s | {fileTime.TotalSeconds:F2} s |");
        described = "";
    }
}

failures += PacketCheck.Run(samples, libs, outDir);
failures += FrameTypeCheck.Run(samples, libs, outDir);
failures += RangeCheck.Run(samples, outDir);
failures += ThreadsCheck.Run(samples, outDir);
failures += ResumeCheck.Run(samples, outDir);

foreach (string reject in rejects)
{
    try
    {
        VideoInput.Open(reject);
        Console.WriteLine($"Not refused: {reject}");
        failures++;
    }
    catch (UnsupportedVideoException e)
    {
        Console.WriteLine($"Refused {Path.GetFileName(reject)}: {e.Message}");
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
    using (var first = new FrameReader.FrameDecoder(path, new FrameReader.FrameDecoderOptions { Autorotate = false }))
    {
        if (!first.Next())
            throw new InvalidOperationException($"FFmpeg decodes no frame of {path}.");
        colorSpace = first.ColorSpace ?? info.ColorSpace ?? "";
        colorRange = first.ColorRange ?? info.ColorRange ?? "";
    }

    return new YuvVideo
    {
        Width = info.Width,
        Height = info.Height,
        FrameRate = info.AverageFrameRate,
        PixelFormat = info.PixelFormat!,
        ColorSpace = colorSpace,
        FullRange = colorRange == "pc",
        TimeBase = info.TimeBase,
        StartPts = info.StartPts ?? 0,
        FrameCount = info.FrameCount,
        // As the file path reads it: ffprobe's "duration" (the format's, printed with six decimals), to the nearest tick.
        Duration = probe.Duration is { } d
            ? TimeSpan.FromTicks((long)Math.Round(double.Parse((d.Ticks / 10 * (1 / 1_000_000.0)).ToString("F6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond))
            : null,
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
    Check("time base", ours.TimeBase, ffmpeg.TimeBase);
    Check("start pts", ours.StartPts, ffmpeg.StartPts);
    Check("frame count", ours.FrameCount, ffmpeg.FrameCount);
    Check("duration", ours.Duration, ffmpeg.Duration);
    Check("codec", ours.Codec, ffmpeg.Codec);
    Check("container", ours.Container, ffmpeg.Container);
    return d;
}
