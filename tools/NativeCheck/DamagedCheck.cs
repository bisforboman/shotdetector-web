using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The damaged frames (decision 2026-11-02-damaged-jpeg.md, the owner's A): a JPEG whose scan runs past its data, which
/// ManagedCodecs decodes with the part it doesn't reach 0 and FFmpeg with a frame from its buffer pool there, which one
/// depending on its threads. The core's damaged frames (<see cref="VideoInput.DamagedFrames"/>) must be FFmpeg's: those
/// its MJPEG decoder logs <c>overread</c> for, one thread, paired with <c>showinfo</c>'s frame number; the stats CSV must
/// equal the FFmpeg path's but for the rows that read a damaged frame (<see cref="Outside"/>); the picture's measures but
/// for the damaged frames themselves (PictureCheck).
/// </summary>
static partial class DamagedCheck
{
    /// <summary>The frames (0-based, display order) FFmpeg's decoder found a scan short in, one thread.</summary>
    public static IReadOnlyList<int> Ffmpeg(string libs, string sample)
    {
        string ffmpeg = Path.Combine(libs, "ffmpeg");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg";
        var start = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { "-hide_banner", "-nostats", "-nostdin", "-threads", "1", "-i", sample, "-map", "0:v:0",
                     "-vf", "showinfo", "-f", "null", "-" })
            start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg didn't start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        string log = process.StandardError.ReadToEnd();
        process.WaitForExit();
        _ = stdout.Result;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {log}");

        // The decoder logs while it decodes a frame, which showinfo then prints: one thread, so in that order.
        var frames = new List<int>();
        bool overread = false;
        foreach (string line in log.Split('\n'))
        {
            if (line.Contains("overread", StringComparison.Ordinal))
                overread = true;
            else if (overread && ShowInfo().Match(line) is { Success: true } m)
            {
                frames.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                overread = false;
            }
        }

        return frames;
    }

    [GeneratedRegex(@"Parsed_showinfo.*\bn:\s*(\d+)")]
    private static partial Regex ShowInfo();

    /// <summary>
    /// The frame numbers (1-based, the stats CSV's) of the rows that read damaged frame <paramref name="damaged"/>
    /// (0-based): its own and the next (each compared with the one before), and the adaptive ratio's
    /// <paramref name="window"/> frames on each side of those; one more on each side when the run
    /// <paramref name="deinterlaced"/> (yadif makes each frame from the one before and the one after too).
    /// </summary>
    public static (int First, int Last) Rows(int damaged, int window, bool deinterlaced)
    {
        int reach = window + (deinterlaced ? 1 : 0);
        return (damaged + 1 - reach, damaged + 2 + reach);
    }

    /// <summary><paramref name="csv"/> without the rows that read a damaged frame (<see cref="Rows"/>): each such row's fields after the frame number and timecode blanked.</summary>
    public static string Outside(string csv, IReadOnlyList<int> damaged, int window, bool deinterlaced)
    {
        if (damaged.Count == 0)
            return csv;
        var lines = csv.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            int comma = lines[i].IndexOf(',');
            if (comma <= 0 || !int.TryParse(lines[i].AsSpan(0, comma), CultureInfo.InvariantCulture, out int number))
                continue;
            if (damaged.Any(d => Rows(d, window, deinterlaced) is var (first, last) && number >= first && number <= last))
                lines[i] = string.Join(',', lines[i].Split(',').Select((field, k) => k < 2 ? field : ""));
        }

        return string.Join('\n', lines);
    }

    /// <summary>The rows <see cref="Outside"/> leaves out, as the report names them.</summary>
    public static string Named(IReadOnlyList<int> damaged, int window, bool deinterlaced) => string.Join(", ",
        damaged.Select(d => Rows(d, window, deinterlaced) is var (first, last) ? $"rows {first}-{last} around frame {d + 1}" : ""));
}
