using ShotDetector;
using Measured = FrameReader.PictureMeasures;

namespace ShotDetectorWeb;

/// <summary>
/// Measures each frame of a run as it is read (docs/decisions/2026-10-30-picture-measures.md): FrameReader's
/// <see cref="FrameReader.PictureMeter"/> (the page's measures moved upstream, shotdetector's decision 2026-10-31) given
/// a <see cref="YuvVideo"/>'s frames as ManagedCodecs decodes them, one pass over each frame before ShotDetector converts
/// it. What it keeps per frame is what FFmpeg's filters count, so that <see cref="Measured"/> gives their figures
/// exactly: <c>signalstats</c>' YMIN, YAVG, YMAX and BRNG, <c>blackdetect</c>'s black pixels, <c>cropdetect</c>'s first
/// and last lines over its limit, and whether the frame is colour bars; with them the decoder's picture type (FFmpeg's
/// <c>pict_type</c>, ManagedCodecs' <see cref="ManagedCodecs.VideoFrame.PictureType"/>). A 10-bit frame is measured in
/// 10-bit code values, as FFmpeg's filters measure it without converting it.
/// </summary>
public sealed class PictureMeter
{
    readonly FrameReader.PictureMeter _meter;

    // The frames given before the range's first (its From to its FirstFrame), which the range before measures.
    int _skip;

    /// <summary>A meter of the whole video's frames, from the first.</summary>
    public PictureMeter(YuvVideo video)
    {
        _meter = new FrameReader.PictureMeter(Format(video));
    }

    /// <summary>
    /// A meter of <paramref name="range"/>'s frames, as <see cref="VideoInput.RangeFramesAsync"/> gives them: from its
    /// <see cref="FrameRange.From"/> (the frames before its first not measured: the range before measures them) to its
    /// <see cref="FrameRange.To"/> (the next range's first on not measured).
    /// </summary>
    public PictureMeter(YuvVideo video, FrameRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        _meter = new FrameReader.PictureMeter(Format(video), range.FirstFrame, range.EndFrame);
        _skip = range.FirstFrame - range.From;
    }

    /// <summary>The frames measured so far.</summary>
    public Measured Measures => _meter.Measures;

    /// <summary>
    /// Measures <paramref name="frame"/>, the next frame given, if it is one of the meter's, and keeps its picture type,
    /// <paramref name="type"/>.
    /// </summary>
    public void Add(YuvFrame frame, ManagedCodecs.PictureType type)
    {
        if (_skip > 0)
        {
            _skip--;
            return;
        }

        _meter.Add(frame.Y.Span, frame.YStride, frame.U.Span, frame.UStride, frame.V.Span, frame.VStride, frame.Pts, Type(type));
    }

    /// <summary>What FrameReader's meter needs to know of <paramref name="video"/>'s frames, as ManagedCodecs decodes them.</summary>
    static FrameReader.PictureFormat Format(YuvVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        var (depth, chroma) = video.PixelFormat switch
        {
            "yuv420p" or "yuvj420p" => (8, FrameReader.ChromaSubsampling.Yuv420),
            "yuv422p" or "yuvj422p" => (8, FrameReader.ChromaSubsampling.Yuv422),
            "yuv420p10le" => (10, FrameReader.ChromaSubsampling.Yuv420),
            _ => throw new ArgumentException($"The pixel format {video.PixelFormat} isn't measured.", nameof(video)),
        };
        return new FrameReader.PictureFormat(video.Width, video.Height, video.FullRange,
            video.TimeBase ?? throw new ArgumentException("The video has no time base.", nameof(video)), video.Rotation, depth, chroma);
    }

    /// <summary>ManagedCodecs' picture type as FrameReader's (both FFmpeg's <c>AVPictureType</c>).</summary>
    static FrameReader.PictureType Type(ManagedCodecs.PictureType type) => type switch
    {
        ManagedCodecs.PictureType.I => FrameReader.PictureType.I,
        ManagedCodecs.PictureType.P => FrameReader.PictureType.P,
        ManagedCodecs.PictureType.B => FrameReader.PictureType.B,
        ManagedCodecs.PictureType.S => FrameReader.PictureType.S,
        ManagedCodecs.PictureType.SI => FrameReader.PictureType.SI,
        ManagedCodecs.PictureType.SP => FrameReader.PictureType.SP,
        ManagedCodecs.PictureType.BI => FrameReader.PictureType.BI,
        _ => FrameReader.PictureType.None,
    };
}
