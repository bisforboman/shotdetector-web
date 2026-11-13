using System.Diagnostics;
using System.Globalization;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// The damaged frames (decision 2026-11-02-damaged-jpeg.md, the owner's A): a JPEG whose scan runs past its data, which
/// ManagedCodecs decodes with the part it doesn't reach 0 and FFmpeg with a frame from its buffer pool there, which one
/// depending on its threads. The core's damaged frames (<see cref="VideoInput.DamagedFrames"/>) must be FFmpeg's: those
/// ffprobe drops with <c>-err_detect explode</c> (<see cref="Ffmpeg"/>); the stats CSV must
/// equal the FFmpeg path's but for the rows that read a damaged frame (<see cref="Outside"/>); the picture's measures but
/// for the damaged frames themselves (PictureCheck).
/// </summary>
static class DamagedCheck
{
    /// <summary>
    /// The frames (0-based, display order) FFmpeg's decoder finds damaged: those ffprobe drops with <c>-err_detect
    /// explode</c> (a JPEG whose scan runs past its data fails then, where FFmpeg otherwise logs an overread and gives
    /// the frame), against its frames without it, by pts (by the packet's dts where a frame has none). ffprobe decodes and prints on one thread, so the answer is the
    /// same every time (ffmpeg's log lines, from its decoder's and its filters' threads, interleave as they come).
    /// </summary>
    public static IReadOnlyList<int> Ffmpeg(string libs, string sample)
    {
        var all = Pts(libs, sample, explode: false);
        var kept = Pts(libs, sample, explode: true).ToHashSet();
        var frames = new List<int>();
        for (int i = 0; i < all.Count; i++)
        {
            if (!kept.Contains(all[i]))
                frames.Add(i);
        }

        return frames;
    }

    /// <summary>Each frame's pts as ffprobe decodes them, in display order.</summary>
    static List<string> Pts(string libs, string sample, bool explode)
    {
        string ffprobe = Path.Combine(libs, "ffprobe");
        if (!File.Exists(ffprobe))
            ffprobe = "ffprobe";
        var start = new ProcessStartInfo(ffprobe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in (string[])["-v", "quiet", .. explode ? ["-err_detect", "explode"] : Array.Empty<string>(),
                     "-select_streams", "v:0", "-show_entries", "frame=pts,pkt_dts", "-of", "csv=p=0", sample])
            start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffprobe didn't start.");
        var stderr = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = stderr.Result;
        // A frame without a pts (an AVI's H.264) by its packet's dts, as ShotDetector's file path labels it.
        var pts = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',') is [var p, var dts, ..] && p == "N/A" ? dts : line.Split(',')[0]).ToList();
        pts.Sort((a, b) => long.Parse(a, CultureInfo.InvariantCulture).CompareTo(long.Parse(b, CultureInfo.InvariantCulture)));
        return pts;
    }

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
