using System.Text.Json.Serialization;
using FrameReader;

namespace ShotDetectorWeb;

/// <summary>
/// The picture's measures as the page shows them (<see cref="PictureMeasures"/>), the App's <c>Picture()</c> JSON and
/// the file NativeCheck writes for app-check.mjs to compare with: the run's frames, the lane's points, blackdetect's
/// spans, the colour bars, BRNG and cropdetect's crop. Frames count from the video's first (display order); times in
/// seconds are FFmpeg's: the colour bars' the frames' pts in the time base, the black spans' and still pictures'
/// ShotDetector's, from the file's start (the earliest stream's, <see cref="ShotDetector.YuvVideo.FileStartPts"/>), as
/// <c>ffmpeg -vf blackdetect,freezedetect</c> prints them (decision 2026-11-04-black-spans-and-times.md).
/// </summary>
/// <param name="Frames">The frames measured: the run's (a stopped run's, those it read).</param>
/// <param name="FullRange">Full range (yuvj420p): blackdetect's threshold is 25, not 37 (102, not 151, at 10 bits), and BRNG means little.</param>
/// <param name="BitDepth">
/// The video's bits per sample, 8 or 10: the levels' code values (0 to 255 or 1023, FFmpeg's at that depth) and the
/// thresholds they were counted against (<see cref="PictureMeter"/>).
/// </param>
/// <param name="Points">The lane's points, a few thousand at most, each over <see cref="PicturePoints.FramesPerPoint"/> frames.</param>
/// <param name="Black">
/// blackdetect's spans with <c>d=0</c>, every one, ShotDetector's (<see cref="ShotDetector.DetectionResult.BlackSpans"/>:
/// of the frames as the run reads them, deinterlaced when it deinterlaces); <see cref="PictureSpan.Long"/> those its default
/// 2 s keeps (<see cref="BlackOf"/>).
/// </param>
/// <param name="Bars">The colour bars' spans (by their definition).</param>
/// <param name="Brng">signalstats' BRNG over the frames.</param>
/// <param name="Levels">signalstats' YMIN, YAVG and YMAX over the frames.</param>
/// <param name="Crop">cropdetect's crop with its defaults after the last frame; null for three frames or fewer.</param>
/// <param name="Freeze">freezedetect's still pictures, or why they weren't looked for (decision 2026-11-01-freeze-spans.md).</param>
public sealed record PictureSummary(
    int Frames,
    bool FullRange,
    int BitDepth,
    PicturePoints Points,
    IReadOnlyList<PictureSpan> Black,
    IReadOnlyList<PictureSpan> Bars,
    PictureBrng Brng,
    PictureLevels Levels,
    CropArea? Crop,
    PictureFreeze Freeze)
{
    /// <summary>The most points the lane gets: a frame each up to this many frames, then several.</summary>
    public const int MostPoints = 2000;

    /// <summary>blackdetect's default <c>d</c>: the card lists the spans this long or longer.</summary>
    public const double BlackSeconds = 2;

    /// <summary>
    /// The summary of <paramref name="m"/>, with <paramref name="black"/>'s spans (<see cref="BlackOf"/>) and
    /// <paramref name="freeze"/>'s still pictures (or why there are none).
    /// </summary>
    public static PictureSummary Of(PictureMeasures m, IReadOnlyList<PictureSpan> black, PictureFreeze freeze)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(black);
        ArgumentNullException.ThrowIfNull(freeze);
        int per = Math.Max(1, (m.Count + MostPoints - 1) / MostPoints);
        int points = (m.Count + per - 1) / per;
        var min = new int[points];
        var mean = new double[points];
        var max = new int[points];
        var brng = new double[points];
        for (int p = 0; p < points; p++)
        {
            int from = p * per, to = Math.Min(m.Count, from + per);
            int lo = int.MaxValue, hi = 0;
            double sum = 0, worst = 0;
            for (int i = from; i < to; i++)
            {
                lo = Math.Min(lo, m.YMin(i));
                hi = Math.Max(hi, m.YMax(i));
                sum += m.YAvg(i);
                worst = Math.Max(worst, m.Brng(i));
            }

            min[p] = lo;
            max[p] = hi;
            mean[p] = Math.Round(sum / (to - from), 2);
            brng[p] = worst;
        }

        var bars = m.BarsSpans().Select(s => Span(m, s, false)).ToArray();

        int brngFrames = 0, largestAt = -1;
        double largest = 0;
        int lowest = int.MaxValue, highest = 0;
        double means = 0;
        for (int i = 0; i < m.Count; i++)
        {
            if (m.BrngPixels(i) > 0)
                brngFrames++;
            if (m.Brng(i) > largest)
                (largest, largestAt) = (m.Brng(i), m.FirstFrame + i);
            lowest = Math.Min(lowest, m.YMin(i));
            highest = Math.Max(highest, m.YMax(i));
            means += m.YAvg(i);
        }

        var levels = m.Count == 0 ? new PictureLevels(null, null, null)
            : new PictureLevels(lowest, Math.Round(means / m.Count, 2), highest);
        return new PictureSummary(m.Count, m.FullRange, m.BitDepth, new PicturePoints(per, min, mean, max, brng), black, bars,
            new PictureBrng(brngFrames, largest, largestAt < 0 ? null : largestAt), levels, m.Crop(), freeze);
    }

    static PictureSpan Span(PictureMeasures m, FrameSpan s, bool isLong, double shift = 0)
    {
        double tb = m.TimeBase.Num / (double)m.TimeBase.Den;
        return new PictureSpan(s.FirstFrame, s.EndFrame, s.StartPts * tb - shift, s.EndPts * tb - shift, isLong);
    }

    /// <summary>
    /// The run's black spans: ShotDetector's (<see cref="ShotDetector.DetectionResult.BlackSpans"/>, <c>d=0</c>), each
    /// <see cref="PictureSpan.Long"/> by blackdetect's own rule for its default 2 s (<c>check_black_end</c>: black_end -
    /// black_start, in the time base of the frames it sees, at least 2 s over that time base truncated; yadif's doubles
    /// the rate); for a run kept before every run looked for them (none: its ranges were scored without), the page's own
    /// from <paramref name="m"/>, from the file's start as ShotDetector's are (<see cref="ShotDetector.YuvVideo.FileStartPts"/>).
    /// </summary>
    public static IReadOnlyList<PictureSpan> BlackOf(ShotDetector.DetectionResult result, PictureMeasures m, ShotDetector.YuvVideo video)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(video);
        double tb = m.TimeBase.Num / (double)m.TimeBase.Den;
        if (result.BlackSpans is { } spans)
        {
            double seen = result.Video.Deinterlaced ? tb / 2 : tb;
            long least = (long)(BlackSeconds / seen);
            return [.. spans.Select(s => new PictureSpan(s.StartFrame, s.EndFrame, s.Start, s.End,
                (long)Math.Round((s.End - s.Start) / seen) >= least))];
        }

        double start = (video.FileStartPts ?? video.StartPts) * tb;
        var longBlack = m.BlackSpans(BlackSeconds).Select(s => s.FirstFrame).ToHashSet();
        return [.. m.BlackSpans(0).Select(s => Span(m, s, longBlack.Contains(s.FirstFrame), start))];
    }
}

/// <summary>
/// The lane's points: point <c>i</c> over frames <c>i * FramesPerPoint</c> on, its lowest YMIN, its frames' mean YAVG
/// (to two decimals: the page's), its highest YMAX and its largest BRNG.
/// </summary>
public sealed record PicturePoints(int FramesPerPoint, int[] Min, double[] Mean, int[] Max, double[] Brng);

/// <summary>
/// A span of frames, <see cref="FirstFrame"/> up to <see cref="EndFrame"/>, from <see cref="Start"/> to <see cref="End"/>
/// seconds as FFmpeg reports it (a black span at the end ends at the last frame's pts, its <see cref="EndFrame"/> that
/// frame's); <see cref="Long"/>: a black span blackdetect's default 2 s keeps.
/// </summary>
public sealed record PictureSpan(int FirstFrame, int EndFrame, double Start, double End, bool Long);

/// <summary>BRNG: the frames with any pixel out of broadcast range, the largest share, and the frame it is in (null for none).</summary>
public sealed record PictureBrng(int Frames, double Largest, int? At);

/// <summary>The lowest YMIN, the mean of the frames' YAVG (to two decimals: the page's) and the highest YMAX; null without frames.</summary>
public sealed record PictureLevels(int? Min, double? Mean, int? Max);

/// <summary>
/// freezedetect's still pictures (its defaults: n=0.001, d=2 s), ShotDetector's (<see cref="ShotDetector.DetectionResult.FreezeSpans"/>,
/// decision 2026-11-03-spans-on-ranges.md): of the frames as the run reads them, deinterlaced when it deinterlaces
/// (<see cref="Deinterlaced"/>); or why they weren't looked for (a stopped run kept before they were).
/// </summary>
/// <param name="Spans">The still pictures, in order; null when not looked for.</param>
/// <param name="NotMeasured">Why not, in the page's words; null when looked for.</param>
/// <param name="Deinterlaced">Whether the frames compared were deinterlaced (the run's).</param>
public sealed record PictureFreeze(IReadOnlyList<PictureStill>? Spans, string? NotMeasured, bool Deinterlaced = false)
{
    /// <summary>A run resumed from ranges kept before still pictures were looked for in every run: none looked for.</summary>
    public static PictureFreeze KeptWithout { get; } = new(null,
        "Not looked for: the stopped part was kept before the page looked for still pictures in every run. A run from the start looks for them.");

    /// <summary>
    /// <paramref name="result"/>'s still pictures, or <see cref="KeptWithout"/> when it didn't look for them, timed as
    /// ShotDetector times them, from the file's start (the earliest stream's), as <c>ffmpeg -vf freezedetect</c> prints them (decision
    /// 2026-11-04-black-spans-and-times.md).
    /// </summary>
    public static PictureFreeze Of(ShotDetector.DetectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.FreezeSpans is { } spans
            ? new([.. spans.Select(s => new PictureStill(s.StartFrame, s.EndFrame, s.Start, s.End))], null, result.Video.Deinterlaced)
            : KeptWithout;
    }
}

/// <summary>
/// A still picture as freezedetect reports it: from its first frame (the one the others were compared with) to the first
/// that differs, <see cref="Start"/> and <see cref="End"/> their pts in seconds (freeze_start, freeze_end); one still
/// running where the frames end has no end (freezedetect prints no freeze_end), <see cref="EndFrame"/> and
/// <see cref="End"/> null.
/// </summary>
public sealed record PictureStill(int FirstFrame, int? EndFrame, double Start, double? End);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PictureSummary))]
public partial class PictureJson : JsonSerializerContext;
