using System.Diagnostics;
using FrameReader;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The frame type check (README, "Checks"; backlog "Frame types (I, P, B) on the strip"): for each sample, every frame's
/// picture type as the core keeps it (<see cref="PictureMeter"/>, ManagedCodecs' <c>VideoFrame.PictureType</c>) against
/// <c>ffprobe -show_entries frame=pict_type</c> (the ffprobe beside SHOTDETECTOR_FFMPEG_LIBS, else the one
/// on PATH), frame by frame in display order, exactly; an MJPEG sample's all I. The same on four decoder threads, and
/// the video split into ranges as the page splits it (at most 4), each measured on its own and joined. ffprobe's types,
/// one character a frame, go to <c>&lt;sample&gt;.frametypes.txt</c> in the output folder, which the wasm check
/// compares the App's <c>FrameTypes()</c> with.
/// </summary>
internal static class FrameTypeCheck
{
    const int MostRanges = 4;
    const int Threads = 4;

    /// <summary>Prints a table with a row per sample; returns how many samples differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string ffmpegDirectory, string outDir)
    {
        string ffprobe = Path.Combine(ffmpegDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(ffprobe))
            ffprobe = "ffprobe";

        int failures = 0;
        Console.WriteLine();
        Console.WriteLine($"| Sample | Frames | I | P | B | Against ffprobe | {Threads} threads | Ranges |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample);
            try
            {
                var input = VideoInput.Open(sample);
                string theirs = Probe(ffprobe, sample);
                File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(sample) + ".frametypes.txt"), theirs);
                string ours = Types(input, 1);
                var problems = new List<string>();
                string against = Compare(ours, theirs, "ffprobe's", problems);
                if (input.Video.Codec == "mjpeg" && ours.Any(t => t != 'I'))
                    problems.Add("an MJPEG frame not I");
                string threaded = Compare(Types(input, Threads), ours, "one thread's", problems);
                string ranges = Ranges(input, ours, problems);
                if (problems.Count > 0)
                {
                    failures++;
                    against += $" **DIFFERS: {string.Join("; ", problems.Take(3))}{(problems.Count > 3 ? $"; {problems.Count - 3} more" : "")}**";
                }

                int Count(char t) => ours.Count(c => c == t);
                Console.WriteLine($"| {name} | {ours.Length} | {Count('I')} | {Count('P')} | {Count('B')} | {against} | {threaded} | {ranges} |");
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | - | - | - | - | failed: {e.GetType().Name}: {e.Message} | - | - |");
                failures++;
            }
        }

        return failures;
    }

    /// <summary>The whole video's frames' types as a run keeps them, decoded on <paramref name="threads"/> threads.</summary>
    static string Types(VideoInput input, int threads)
    {
        input.DecoderThreads = threads;
        try
        {
            var meter = new PictureMeter(input.Video);
            Drain(input.FramesAsync(int.MaxValue, _ => ValueTask.FromResult(true), picture: meter));
            return meter.Measures.Types();
        }
        finally
        {
            input.DecoderThreads = 1;
        }
    }

    /// <summary>The ranges' types, each range measured on its own, joined: the whole video's.</summary>
    static string Ranges(VideoInput input, string whole, List<string> problems)
    {
        var ranges = input.Ranges(null, MostRanges);
        var parts = new List<PictureMeasures>();
        foreach (var range in ranges)
        {
            var meter = new PictureMeter(input.Video, range);
            Drain(input.RangeFramesAsync(range, int.MaxValue, _ => ValueTask.FromResult(true), picture: meter));
            parts.Add(PictureMeasures.FromBytes(meter.Measures.ToBytes()));
        }

        string joined = PictureMeasures.Join(parts)?.Types() ?? "";
        return $"{ranges.Count}: {Compare(joined, whole, "the whole video's", problems)}";
    }

    static void Drain(IAsyncEnumerable<ShotDetector.YuvFrame> frames)
    {
        var e = frames.GetAsyncEnumerator();
        try
        {
            while (e.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
            }
        }
        finally
        {
            e.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>"same", or what differs (added to <paramref name="problems"/>): the counts, the first frames that differ.</summary>
    static string Compare(string ours, string theirs, string whose, List<string> problems)
    {
        var d = new List<string>();
        if (ours.Length != theirs.Length)
            d.Add($"{ours.Length} frames vs {whose} {theirs.Length}");
        for (int i = 0; i < Math.Min(ours.Length, theirs.Length) && d.Count < 4; i++)
        {
            if (ours[i] != theirs[i])
                d.Add($"frame {i}: {ours[i]} vs {whose} {theirs[i]}");
        }

        problems.AddRange(d);
        return d.Count == 0 ? "same" : "DIFFERS";
    }

    /// <summary>
    /// Each video frame's pict_type as ffprobe prints it, in output order (display order), one character a frame. Only
    /// the line's first field: ffprobe's csv can add a frame's side data as more fields (x264's first frame's SEI); and no
    /// -show_frames, whose side data a rotated video's frames print over lines of their own (the display matrix).
    /// </summary>
    static string Probe(string ffprobe, string sample)
    {
        var start = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in (string[])["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=pict_type", "-of", "csv=p=0", sample])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe exited with {process.ExitCode}: {error.Result.Trim()}");

        var types = new System.Text.StringBuilder();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string type = line.Split(',')[0];
            if (type.Length != 1)
                throw new InvalidOperationException($"ffprobe printed a pict_type of '{type}'");
            types.Append(type);
        }

        return types.ToString();
    }
}
