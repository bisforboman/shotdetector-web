using System.Diagnostics;
using System.Globalization;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The packet check (README, "Checks"): for each sample, <see cref="VideoInput.Packets"/> against
/// <c>ffprobe -show_packets -select_streams v:0</c> (the ffprobe beside SHOTDETECTOR_FFMPEG_LIBS, as BtbN's shared build
/// has it, else the one on PATH): the same packets in the same order, each with the same pts, pts_time, size and K and D
/// flags, exactly. ffprobe's lines go to <c>&lt;sample&gt;.packets.txt</c> in the output folder, which the wasm check
/// compares the App's <c>Packets()</c> with.
/// </summary>
internal static class PacketCheck
{
    /// <summary>Prints a table with a row per sample; returns how many samples differ or failed.</summary>
    public static int Run(IEnumerable<string> samples, string ffmpegDirectory, string outDir)
    {
        string ffprobe = Path.Combine(ffmpegDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(ffprobe))
            ffprobe = "ffprobe";

        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Video packets | Keyframes | Discarded | Bytes | Against ffprobe |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (string sample in samples)
        {
            string name = Path.GetFileName(sample);
            VideoPackets ours;
            List<string[]> theirs;
            try
            {
                ours = VideoInput.Open(sample).Packets;
                theirs = Probe(ffprobe, sample, Path.Combine(outDir, Path.GetFileNameWithoutExtension(sample) + ".packets.txt"));
            }
            catch (Exception e)
            {
                Console.WriteLine($"| {name} | - | - | - | - | failed: {e.GetType().Name}: {e.Message} |");
                failures++;
                continue;
            }

            var differences = Differences(ours, theirs);
            if (differences.Count > 0)
                failures++;
            string result = differences.Count == 0 ? "same" : $"DIFFERS: {string.Join("; ", differences.Take(3))}{(differences.Count > 3 ? $"; {differences.Count - 3} more" : "")}";
            int keyframes = 0, discarded = 0;
            long bytes = 0;
            for (int i = 0; i < ours.Count; i++)
            {
                keyframes += ours.Flags[i].HasFlag(PacketFlags.Keyframe) ? 1 : 0;
                discarded += ours.Flags[i].HasFlag(PacketFlags.Discard) ? 1 : 0;
                bytes += ours.Sizes[i];
            }

            Console.WriteLine($"| {name} | {ours.Count} | {keyframes} | {discarded} | {bytes:N0} | {result} |");
        }

        return failures;
    }

    /// <summary>
    /// Each video packet as ffprobe prints it: pts, pts_time, size, flags (K, D and C or '_'); its output written to
    /// <paramref name="path"/> as it is.
    /// </summary>
    static List<string[]> Probe(string ffprobe, string sample, string path)
    {
        var start = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in (string[])["-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts,pts_time,size,flags", "-of", "compact=p=0:nk=0", sample])
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe exited with {process.ExitCode}: {error.Result.Trim()}");
        File.WriteAllText(path, output);

        var packets = new List<string[]>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('|').Select(f => f.Split('=', 2)).ToDictionary(f => f[0], f => f.Length > 1 ? f[1] : "");
            packets.Add([fields["pts"], fields["pts_time"], fields["size"], fields["flags"]]);
        }

        return packets;
    }

    static List<string> Differences(VideoPackets ours, List<string[]> theirs)
    {
        var d = new List<string>();
        if (ours.Count != theirs.Count)
            d.Add($"{ours.Count} packets vs ffprobe's {theirs.Count}");
        for (int i = 0; i < Math.Min(ours.Count, theirs.Count); i++)
        {
            var (pts, ptsTime, size, flags) = (theirs[i][0], theirs[i][1], theirs[i][2], theirs[i][3]);
            void Check(string what, string a, string b)
            {
                if (a != b)
                    d.Add($"packet {i}: {what} {a} vs ffprobe's {b}");
            }

            Check("pts", ours.Pts[i].ToString(CultureInfo.InvariantCulture), pts);
            Check("pts_time", ours.Seconds(i).ToString("F6", CultureInfo.InvariantCulture), ptsTime);
            Check("size", ours.Sizes[i].ToString(CultureInfo.InvariantCulture), size);
            Check("K", ours.Flags[i].HasFlag(PacketFlags.Keyframe) ? "K" : "_", flags[..1]);
            Check("D", ours.Flags[i].HasFlag(PacketFlags.Discard) ? "D" : "_", flags.Substring(1, 1));
        }

        return d;
    }
}
