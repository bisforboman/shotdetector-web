using System.Diagnostics;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The field check (README, "Checks"; backlog "Deinterlacing interlaced H.264"): for each sample, every frame's interlaced
/// and top-field-first flags as the core gives them to ShotDetector (<c>YuvFrame.Interlaced</c> and <c>TopFieldFirst</c>,
/// ManagedCodecs' <c>VideoFrame.IsInterlaced</c> and <c>IsTopFieldFirst</c>), which set yadif's parity when a run
/// deinterlaces, against <c>ffprobe -show_entries frame=interlaced_frame,top_field_first</c> (the ffprobe beside
/// SHOTDETECTOR_FFMPEG_LIBS, else the one on PATH), frame by frame in display order, exactly. The stream's field order is
/// the description's, checked with the other fields against FFmpeg's.
/// </summary>
internal static class FieldCheck
{
    /// <summary>Prints a table with a row per sample; returns how many samples differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string ffmpegDirectory)
    {
        string ffprobe = Path.Combine(ffmpegDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(ffprobe))
            ffprobe = "ffprobe";

        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Field order | Frames | Interlaced | Top field first | Against ffprobe |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample);
            try
            {
                var input = VideoInput.Open(sample);
                var ours = input.Frames().Select(f => (f.Interlaced, f.TopFieldFirst)).ToList();
                var theirs = Probe(ffprobe, sample);
                int first = Enumerable.Range(0, Math.Min(ours.Count, theirs.Count)).FirstOrDefault(i => ours[i] != theirs[i], -1);
                string against = ours.Count != theirs.Count ? $"**DIFFERS: {ours.Count} frames, ffprobe's {theirs.Count}**"
                    : first >= 0 ? $"**DIFFERS: frame {first} is {Flags(ours[first])}, ffprobe's {Flags(theirs[first])}**"
                    : "same";
                if (against != "same")
                    failures++;
                Console.WriteLine($"| {name} | {input.Video.FieldOrder} | {ours.Count} | {ours.Count(f => f.Interlaced)} | {ours.Count(f => f.TopFieldFirst)} | {against} |");
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | - | - | - | - | failed: {e.GetType().Name}: {e.Message} |");
                failures++;
            }
        }

        return failures;
    }

    static string Flags((bool Interlaced, bool TopFieldFirst) f) => $"{(f.Interlaced ? 1 : 0)},{(f.TopFieldFirst ? 1 : 0)}";

    /// <summary>
    /// Each video frame's interlaced_frame and top_field_first as ffprobe prints them, in output order (display order):
    /// the line's first two fields (ffprobe's csv can add a frame's side data as more), and no -show_frames (whose side
    /// data a rotated video's frames print over lines of their own).
    /// </summary>
    static List<(bool Interlaced, bool TopFieldFirst)> Probe(string ffprobe, string sample)
    {
        var start = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in (string[])["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=interlaced_frame,top_field_first", "-of", "csv=p=0", sample])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe exited with {process.ExitCode}: {error.Result.Trim()}");

        var flags = new List<(bool, bool)>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(',');
            if (fields.Length < 2 || fields[0] is not ("0" or "1") || fields[1] is not ("0" or "1"))
                throw new InvalidOperationException($"ffprobe printed '{line}' for a frame's interlaced_frame,top_field_first");
            flags.Add((fields[0] == "1", fields[1] == "1"));
        }

        return flags;
    }
}
