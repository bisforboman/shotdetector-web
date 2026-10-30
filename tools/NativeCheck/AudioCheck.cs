// The audio check (README, "Checks"): for each sample with audio (an AVI's answered with a reason: not measured), ShotDetectorWeb.Core's Mp4Audio (ManagedCodecs' AAC
// decoder, no FFmpeg) against FFmpeg's measures of the same file, within tolerances, since the decoded samples aren't
// FFmpeg's to the bit (3e-7) and the measures aren't computed by the same code:
// - the waveform against FrameReader's WaveformData.Read (FFmpeg decoding): the same points, each value within 1;
// - the reference tone against ShotDetector's FindReferenceTone: the same segments, each end within a packet, the level
//   within 0.1 dB;
// - silences against FFmpeg's silencedetect, at its default 2 s and at 1 s: the same spans, each end within a packet;
// - integrated loudness and loudness range against FFmpeg's ebur128 (within 0.1 LU), true and sample peaks per channel
//   against its peak=true+sample (within 0.1 dB).
// On the tone sample both tones must come out at -20 dBFS (within 0.1 dB). Each file in no-aac/ beside the samples
// (make-samples.sh: audio that isn't AAC, or that ManagedCodecs' decoder refuses) must be answered with the reason it
// isn't measured, not an exception. A Markdown table on standard output; each sample's waveform to the output folder
// (<sample>.waveform.json: samplesPerPoint, min and max per waveform), which the wasm check compares its own with.
using System.Diagnostics;
using System.Globalization;
using FrameReader;
using ShotDetector;

namespace ShotDetectorWeb.Checks;

static class AudioCheck
{
    const double LoudnessTolerance = 0.1;
    const double LevelTolerance = 0.1;

    /// <summary>Checks each sample with audio, and the files in no-aac/ beside them; returns how many disagreed.</summary>
    public static int Run(IReadOnlyList<string> samples, string libs, string outDir)
    {
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Audio | Waveform | Reference tone | Silences (2 s; 1 s) | Integrated | Range | True peak | Sample peak | Core time |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
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
            while (cells.Count < 9)
                cells.Add("");
            if (problems.Count > 0)
                cells[0] += " **DIFFERS: " + string.Join("; ", problems) + "**";
            Console.WriteLine($"| {Path.GetFileName(sample)} | {string.Join(" | ", cells)} |");
        }

        string noAac = Path.Combine(Path.GetDirectoryName(samples.FirstOrDefault()) ?? "", "no-aac");
        foreach (string file in Directory.Exists(noAac) ? Directory.GetFiles(noAac).Order() : Enumerable.Empty<string>())
        {
            try
            {
                var measures = Mp4Audio.Measure(file);
                Console.WriteLine(measures.NotMeasured is { } reason ? $"Not measured {Path.GetFileName(file)}: {reason}" : $"Measured, not refused: {file}");
                if (measures.NotMeasured is null)
                    failures++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Not measured {Path.GetFileName(file)} with {e.GetType().Name}, not a reason: {e.Message}");
                failures++;
            }
        }

        return failures;
    }

    static void Check(string sample, string libs, string outDir, List<string> problems, List<string> cells)
    {
        bool hasAudio = Probe(libs, sample, "-select_streams", "a", "-show_entries", "stream=index").Length > 0;
        var watch = Stopwatch.StartNew();
        var ours = Mp4Audio.Measure(sample);
        var time = watch.Elapsed;
        if (ours.Track is not { } track)
        {
            cells.Add(ours.NotMeasured ?? "");
            // An AVI's audio is described (AudioStreamCheck), not measured: a reason, not a measure, is right there.
            if (hasAudio && !(ours.NotMeasured is not null && Path.GetExtension(sample) == ".avi"))
                problems.Add("FFmpeg finds an audio stream");
            return;
        }

        if (!hasAudio)
            problems.Add("FFmpeg finds no audio stream");
        cells.Add($"{track.Codec}, {track.SampleRate} Hz, {track.Channels} ch, {track.Duration.TotalSeconds:F3} s from {track.Start.TotalSeconds:F3} s");
        double packet = track.PacketSamples / (double)track.SampleRate;

        // The waveform, against FrameReader's from FFmpeg's decode with the same points.
        var waveform = ours.Waveform!;
        WriteWaveform(Path.Combine(outDir, Path.GetFileNameWithoutExtension(sample) + ".waveform.json"), waveform);
        using (var reader = new AudioReader(sample))
        {
            var theirs = WaveformData.Read(reader, new WaveformOptions { SamplesPerPixel = waveform.SamplesPerPixel });
            if ((waveform.SampleRate, waveform.Channels, waveform.Length) != (theirs.SampleRate, theirs.Channels, theirs.Length))
            {
                problems.Add($"waveform {waveform.SampleRate} Hz x {waveform.Channels} x {waveform.Length} points vs FrameReader's {theirs.SampleRate} x {theirs.Channels} x {theirs.Length}");
                cells.Add("DIFFERS");
            }
            else
            {
                int largest = 0, differing = 0;
                for (int c = 0; c < waveform.Channels; c++)
                {
                    for (int i = 0; i < waveform.Length; i++)
                    {
                        int d = Math.Max(Math.Abs(waveform.Min(c, i) - theirs.Min(c, i)), Math.Abs(waveform.Max(c, i) - theirs.Max(c, i)));
                        largest = Math.Max(largest, d);
                        if (d > 0)
                            differing++;
                    }
                }

                if (largest > 1)
                    problems.Add($"waveform values up to {largest} apart");
                cells.Add($"{waveform.Length} points of {waveform.SamplesPerPixel}; {differing} differ, by up to {largest}");
            }
        }

        // The reference tone, against ShotDetector's own search on the FFmpeg path.
        var tone = ShotDetection.FindReferenceTone(sample, new DetectionOptions { FfmpegDirectory = libs });
        var toneCell = new List<string>();
        if (tone.Count != ours.ReferenceTone.Count)
            problems.Add($"{ours.ReferenceTone.Count} tone segments vs FindReferenceTone's {tone.Count}");
        for (int i = 0; i < Math.Min(tone.Count, ours.ReferenceTone.Count); i++)
        {
            var (a, b) = (ours.ReferenceTone[i], tone[i]);
            double start = (a.Start - b.Start).TotalSeconds, end = (a.End - b.End).TotalSeconds, level = a.Level - b.Level;
            if (Math.Abs(start) > packet || Math.Abs(end) > packet || Math.Abs(level) > LevelTolerance)
                problems.Add($"tone {i}: {Segment(a)} vs FindReferenceTone's {Segment(b)}");
            toneCell.Add($"{Segment(a)} (start {start * 1000:+0;-0;0} ms, end {end * 1000:+0;-0;0} ms, level {level:+0.000;-0.000;0.000} dB)");
        }

        if (Path.GetFileName(sample).StartsWith("tone-", StringComparison.Ordinal)
            && (ours.ReferenceTone.Count != 2 || ours.ReferenceTone.Any(s => Math.Abs(s.Level + 20) > LevelTolerance)))
            problems.Add("the tone sample's two tones aren't both at -20 dBFS");
        cells.Add(toneCell.Count == 0 ? "none" : string.Join("; ", toneCell));

        // Silences (at 2 s and at 1 s) and loudness, against FFmpeg's filters in one run.
        var ffmpeg = Ffmpeg(libs, sample, track.Channels);
        var shorter = Mp4Audio.Measure(sample, new AudioOptions { SilenceDuration = TimeSpan.FromSeconds(1) }).Silences;
        var silenceCells = new List<string>();
        foreach (var (name, mine, theirs) in new[] { ("2 s", ours.Silences, ffmpeg.Silences2), ("1 s", shorter, ffmpeg.Silences1) })
        {
            if (mine.Count != theirs.Count)
                problems.Add($"{mine.Count} silences of {name} vs silencedetect's {theirs.Count}");
            var spans = new List<string>();
            for (int i = 0; i < Math.Min(mine.Count, theirs.Count); i++)
            {
                double start = (mine[i].Start - theirs[i].Start).TotalSeconds, end = (mine[i].End - theirs[i].End).TotalSeconds;
                if (Math.Abs(start) > packet || Math.Abs(end) > packet)
                    problems.Add($"silence {name} {i}: {Span(mine[i])} vs silencedetect's {Span(theirs[i])}");
                spans.Add($"{Span(mine[i])} ({start * 1000:+0;-0;0}/{end * 1000:+0;-0;0} ms)");
            }

            silenceCells.Add(spans.Count == 0 ? "none" : string.Join(", ", spans));
        }

        cells.Add(string.Join("; ", silenceCells));

        var loudness = ours.Loudness!;
        cells.Add(Compare("integrated", loudness.Integrated, ffmpeg.Integrated, LoudnessTolerance, "LUFS", problems));
        cells.Add(Compare("range", loudness.Range, ffmpeg.Range, LoudnessTolerance, "LU", problems));
        cells.Add(string.Join(", ", ours.Peaks.Select((p, c) => Compare($"true peak {c}", p.True, ffmpeg.TruePeaks[c], LevelTolerance, "dBTP", problems))));
        cells.Add(string.Join(", ", ours.Peaks.Select((p, c) => Compare($"sample peak {c}", p.Sample, ffmpeg.SamplePeaks[c], LevelTolerance, "dBFS", problems)))
            + $"; full scale {string.Join("/", ours.Peaks.Select(p => p.FullScale))}");
        cells.Add($"{time.TotalSeconds:F2} s");
    }

    static string Compare(string name, double? ours, double? ffmpeg, double tolerance, string unit, List<string> problems)
    {
        if (ours is null || ffmpeg is null || double.IsInfinity(ours.Value) || double.IsInfinity(ffmpeg.Value))
        {
            if (ours != ffmpeg)
                problems.Add($"{name} {ours} vs FFmpeg's {ffmpeg}");
            return $"{ours?.ToString("F2", CultureInfo.InvariantCulture) ?? "none"} {unit}";
        }

        double d = ours.Value - ffmpeg.Value;
        if (Math.Abs(d) > tolerance)
            problems.Add($"{name} {ours:F3} vs FFmpeg's {ffmpeg:F3} {unit}");
        return $"{ours:F2} {unit} ({d:+0.000;-0.000;0.000})";
    }

    static string Segment(ToneSegment s) => $"{s.Start.TotalSeconds:F2}-{s.End.TotalSeconds:F2} s at {s.Level:F2} dBFS";

    static string Span(Silence s) => $"{s.Start.TotalSeconds:F3}-{s.End.TotalSeconds:F3} s";

    sealed record FfmpegMeasures(
        List<Silence> Silences2,
        List<Silence> Silences1,
        double? Integrated,
        double Range,
        double?[] TruePeaks,
        double?[] SamplePeaks);

    /// <summary>
    /// FFmpeg's silencedetect (2 s and 1 s) and ebur128=peak=true+sample on the first audio stream, through the ffmpeg
    /// beside the libraries. The loudness comes from ebur128's frame metadata (three decimals; its summary has one) after
    /// the last frame; its peaks there are linear. silencedetect's times count from the file's start time, as ffmpeg
    /// shifts every stream: moved back onto the file's timeline here.
    /// </summary>
    static FfmpegMeasures Ffmpeg(string libs, string sample, int channels)
    {
        double formatStart = double.Parse(Probe(libs, sample, "-show_entries", "format=start_time").Trim(), CultureInfo.InvariantCulture);
        string metadata = Path.GetTempFileName();
        try
        {
            string graph = $"silencedetect=d=2,silencedetect=d=1,ebur128=peak=true+sample:metadata=1:framelog=quiet,ametadata=mode=print:file={metadata}";
            string log = Run(Path.Combine(libs, "ffmpeg"), "-hide_banner", "-nostats", "-nostdin", "-i", sample, "-map", "0:a:0", "-af", graph, "-f", "null", "-");
            var silences = new Dictionary<string, List<Silence>>();
            var open = new Dictionary<string, double>();
            foreach (string line in log.Split('\n'))
            {
                // [Parsed_silencedetect_0 @ 0x...] silence_start: 6.001854
                // [Parsed_silencedetect_0 @ 0x...] silence_end: 7.998417 | silence_duration: 1.996563
                int at = line.IndexOf("[Parsed_silencedetect_", StringComparison.Ordinal);
                if (at < 0)
                    continue;
                string filter = line[(at + 1)..line.IndexOf(' ', at)];
                string[] words = line[(line.IndexOf(']', at) + 1)..].Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries);
                if (words[0] == "silence_start:")
                {
                    open[filter] = double.Parse(words[1], CultureInfo.InvariantCulture);
                }
                else if (words[0] == "silence_end:")
                {
                    double end = double.Parse(words[1], CultureInfo.InvariantCulture);
                    if (!silences.TryGetValue(filter, out var list))
                        silences[filter] = list = [];
                    list.Add(new Silence(At(open[filter] + formatStart), At(end + formatStart)));
                }
            }

            var last = new Dictionary<string, string>();
            foreach (string line in File.ReadLines(metadata))
            {
                int eq = line.IndexOf('=');
                if (line.StartsWith("lavfi.r128.", StringComparison.Ordinal) && eq > 0)
                    last[line[11..eq]] = line[(eq + 1)..];
            }

            double? Value(string key) => last.TryGetValue(key, out string? v) ? double.Parse(v, CultureInfo.InvariantCulture) : null;
            double? integrated = Value("I");
            return new FfmpegMeasures(
                silences.GetValueOrDefault("Parsed_silencedetect_0") ?? [],
                silences.GetValueOrDefault("Parsed_silencedetect_1") ?? [],
                integrated <= -70 ? null : integrated, // ebur128 starts at -70 and stays there with nothing above the gate
                Value("LRA") ?? 0,
                [.. Enumerable.Range(0, channels).Select(c => Value($"true_peaks_ch{c}") is { } p ? 20 * Math.Log10(p) : (double?)null)],
                [.. Enumerable.Range(0, channels).Select(c => Value($"sample_peaks_ch{c}") is { } p ? 20 * Math.Log10(p) : (double?)null)]);
        }
        finally
        {
            File.Delete(metadata);
        }
    }

    static TimeSpan At(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    static string Probe(string libs, string sample, params string[] args) =>
        Run(Path.Combine(libs, "ffprobe"), ["-v", "error", .. args, "-of", "csv=p=0", sample]);

    /// <summary>The waveform's points as the App's Audio() gives them (min and max per waveform), for tools/app-check.mjs.</summary>
    static void WriteWaveform(string path, WaveformData w)
    {
        string Values(Func<int, int, short> value) => "[" + string.Join(",", Enumerable.Range(0, w.Channels).Select(c =>
            "[" + string.Join(",", Enumerable.Range(0, w.Length).Select(i => value(c, i).ToString(CultureInfo.InvariantCulture))) + "]")) + "]";
        File.WriteAllText(path, $"{{\"samplesPerPoint\":{w.SamplesPerPixel},\"min\":{Values(w.Min)},\"max\":{Values(w.Max)}}}\n");
    }

    /// <summary>Runs a program and returns its standard output and error, failing unless it exits with 0.</summary>
    static string Run(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(program)} exited with {process.ExitCode}: {error.Result}");
        return output + error.Result;
    }
}
