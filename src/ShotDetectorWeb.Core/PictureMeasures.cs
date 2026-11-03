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
/// 10-bit code values, as FFmpeg's filters measure it without converting it. A meter of a run that reads every frame in
/// order from the first can also find the still pictures, as FFmpeg's <c>freezedetect</c> does (FrameReader's
/// <see cref="FrameReader.FreezeDetector"/>; decision 2026-11-01-freeze-spans.md): it compares each frame with a still
/// picture's first, however far back, so a range from a later frame can't.
/// </summary>
public sealed class PictureMeter
{
    readonly FrameReader.PictureMeter _meter;
    readonly FrameReader.FreezeDetector? _freeze;

    // The frames given before the range's first (its From to its FirstFrame), which the range before measures.
    int _skip;

    // The frames the freeze detector is given: the range's own (the next range's first on, the overlap, left out).
    int _freezeFrames;

    /// <summary>A meter of the whole video's frames, from the first; finding the still pictures too when <paramref name="freeze"/>.</summary>
    public PictureMeter(YuvVideo video, bool freeze = false)
    {
        _meter = new FrameReader.PictureMeter(Format(video));
        if (freeze)
            (_freeze, _freezeFrames) = (Freeze(video), int.MaxValue);
    }

    /// <summary>
    /// A meter of <paramref name="range"/>'s frames, as <see cref="VideoInput.RangeFramesAsync"/> gives them: from its
    /// <see cref="FrameRange.From"/> (the frames before its first not measured: the range before measures them) to its
    /// <see cref="FrameRange.To"/> (the next range's first on not measured).
    /// </summary>
    /// <remarks>
    /// With <paramref name="freeze"/>, it also finds the still pictures in the range's frames: only for a range from frame
    /// 0 (an <see cref="ArgumentException"/> otherwise), the run's one range or the first of a split run.
    /// </remarks>
    public PictureMeter(YuvVideo video, FrameRange range, bool freeze = false)
    {
        ArgumentNullException.ThrowIfNull(range);
        _meter = new FrameReader.PictureMeter(Format(video), range.FirstFrame, range.EndFrame);
        _skip = range.FirstFrame - range.From;
        if (freeze)
        {
            if (range.FirstFrame != 0 || range.From != 0)
                throw new ArgumentException("Still pictures are found only from frame 0: freezedetect compares across ranges.", nameof(freeze));
            (_freeze, _freezeFrames) = (Freeze(video), range.EndFrame);
        }
    }

    /// <summary>The frames measured so far.</summary>
    public Measured Measures => _meter.Measures;

    /// <summary>Whether the meter finds the still pictures (<see cref="FreezeSpans"/>).</summary>
    public bool FindsFreezes => _freeze is not null;

    /// <summary>
    /// The still pictures in the first <paramref name="frames"/> frames (a stopped run's, those it read), as
    /// <c>freezedetect</c> reports them by then: those that ended, and one still running there without an end; null when
    /// the meter doesn't find them. Once only.
    /// </summary>
    public IReadOnlyList<FrameReader.FrozenSpan>? FreezeSpans(int frames) => _freeze?.Finish(frames);

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
        if (_freeze is not null && _freezeFrames > 0)
        {
            _freezeFrames--;
            _freeze.Add(frame.Pts, frame.Y.Span, frame.YStride, frame.U.Span, frame.UStride, frame.V.Span, frame.VStride, default, 0);
        }
    }

    /// <summary>
    /// FrameReader's freezedetect for <paramref name="video"/>'s frames as ManagedCodecs decodes them, with freezedetect's
    /// defaults: its planes as freezedetect sizes them, each line in samples, the chroma half as wide (rounded up) and,
    /// for 4:2:0, half as high (rounded down: an odd height's last chroma row left out, as freezedetect leaves it).
    /// </summary>
    static FrameReader.FreezeDetector Freeze(YuvVideo video)
    {
        var format = Format(video);
        int w = video.Width, h = video.Height, cw = (w + 1) >> 1;
        int ch = format.Chroma == FrameReader.ChromaSubsampling.Yuv420 ? h >> 1 : h;
        return new FrameReader.FreezeDetector([(w, h), (cw, ch), (cw, ch)], format.BitDepth, format.TimeBase, null);
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
