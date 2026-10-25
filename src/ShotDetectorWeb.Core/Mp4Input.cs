using System.Globalization;
using ManagedCodecs;
using ManagedCodecs.Containers.Mp4;
using ManagedCodecs.H264;
using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// An H.264 video in an MP4 file, decoded with ManagedCodecs (no FFmpeg), described as FFmpeg describes it, so that
/// <see cref="ShotDetection.Detect(YuvVideo, IEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/> gives the
/// file's result, byte for byte, as <see cref="ShotDetection.Detect(string, DetectionOptions?, CancellationToken)"/>
/// with FFmpeg and the same converter does.
/// </summary>
/// <remarks>
/// So far: 8-bit 4:2:0 H.264 (progressive) in a non-fragmented MP4, at a constant frame rate, not rotated; anything else
/// throws <see cref="UnsupportedVideoException"/> when opened. Damaged files throw ManagedCodecs'
/// <see cref="InvalidDataException"/>.
/// </remarks>
public sealed class Mp4Input
{
    // FFmpeg's names of the H.264 / ISO matrix coefficients (libavutil's av_color_space_name); unspecified (2) is "".
    static readonly string[] MatrixNames =
    [
        "gbr", "bt709", "", "reserved", "fcc", "bt470bg", "smpte170m", "smpte240m", "ycgco", "bt2020nc", "bt2020c",
        "smpte2085", "chroma-derived-nc", "chroma-derived-c", "ictcp", "ipt-c2", "ycgco-re", "ycgco-ro",
    ];

    readonly Func<Stream> _open;

    Mp4Input(Func<Stream> open)
    {
        _open = open;
        using var stream = open();
        var header = Mp4Header.Read(stream);
        if (header.Fragmented)
            throw new UnsupportedVideoException("Fragmented MP4 isn't supported yet.");
        if (header.SampleEntry is not ("avc1" or "avc3"))
            throw new UnsupportedVideoException($"The video is '{header.SampleEntry}', not H.264 ('avc1'): only H.264 is supported so far.");
        if (header.Transformed)
            throw new UnsupportedVideoException("The video is rotated or flipped (its track matrix): not supported yet.");
        if (header.SampleCount == 0 || header.TotalDuration <= 0 || header.Timescale == 0)
            throw new UnsupportedVideoException("The video track has no samples.");
        CheckConstantRate(header);

        var sps = SpsInfo.FromAvcConfig(header.AvcConfig.Span);
        if (sps.ChromaFormatIdc != 1 || sps.BitDepthLuma != 8 || sps.BitDepthChroma != 8)
        {
            string chroma = sps.ChromaFormatIdc switch { 0 => "4:0:0", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };
            throw new UnsupportedVideoException($"The video is {chroma} at {sps.BitDepthLuma} bits: only 8-bit 4:2:0 is supported so far.");
        }

        if (!sps.FrameMbsOnly)
            throw new UnsupportedVideoException("The video is interlaced (field or MBAFF coding): not supported yet.");

        // The first frame's size (after the SPS's cropping) and timestamp: in display order, the smallest pts, which is
        // the stream's start (FFmpeg's start_time for an MP4 video track, edit list applied).
        var (width, height, firstPts) = FirstFrame();

        // The decoder's colour tags: the SPS's VUI where it has them, else the container's 'colr' box (FFmpeg's h264
        // decoder overrides the container's values with the SPS's). Full range makes FFmpeg's h264 decoder output yuvj420p.
        bool fullRange = sps.VideoSignalTypePresent ? sps.FullRange : header.ColrFullRange ?? false;
        int? matrix = sps.MatrixCoefficients ?? header.ColrMatrix;
        string colorSpace = matrix is int m && m < MatrixNames.Length ? MatrixNames[m] : "";

        var rate = ManagedCodecs.Rational.Reduce(header.Timescale * header.SampleCount, header.TotalDuration);
        Video = new YuvVideo
        {
            Width = width,
            Height = height,
            FrameRate = new Fps(rate.Numerator, rate.Denominator),
            PixelFormat = fullRange ? "yuvj420p" : "yuv420p",
            ColorSpace = colorSpace,
            FullRange = fullRange,
            TimeBase = new FrameReader.Rational(1, checked((int)header.Timescale)),
            StartPts = firstPts,
            FrameCount = header.SampleCount,
            Duration = FormatDuration() is long us
                ? Seconds((us * (1 / 1_000_000.0)).ToString("F6", CultureInfo.InvariantCulture))
                : null,
            Codec = "h264",
            Container = "mov,mp4,m4a,3gp,3g2,mj2",
        };
    }

    /// <summary>The video as FFmpeg's demuxer and decoder report it (average frame rate, time base, start pts, colour tags, frame count).</summary>
    public YuvVideo Video { get; }

    /// <summary>Opens an MP4 file on disk.</summary>
    public static Mp4Input Open(string path) => new(() => File.OpenRead(path));

    /// <summary>Opens an MP4 file held in memory (in the browser: the file the user picked).</summary>
    public static Mp4Input Open(byte[] data) => new(() => new MemoryStream(data, writable: false));

    /// <summary>
    /// Opens an MP4 file read through <paramref name="open"/>, which must give a new readable, seekable stream over the
    /// file on every call (in the browser: one reading slices of the picked file, so it never has to fit in memory).
    /// </summary>
    public static Mp4Input Open(Func<Stream> open) => new(open);

    /// <summary>
    /// Detects shots: <see cref="ShotDetection.Detect(YuvVideo, IEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/>
    /// on <see cref="Frames"/>, with ShotDetector.FastYuv's converter unless the options name another.
    /// </summary>
    public DetectionResult Detect(DetectionOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new DetectionOptions();
        if (options.Yuv420Converter is null)
            options = options with { Yuv420Converter = new SwscaleYuv420() };
        return ShotDetection.Detect(Video, Frames(), options, cancellationToken);
    }

    /// <summary>
    /// The decoded frames in display order, each valid until the enumeration moves on. Every enumeration decodes the file
    /// again from its start.
    /// </summary>
    public IEnumerable<YuvFrame> Frames()
    {
        foreach (var frame in DecodedFrames())
        {
            using (frame)
            {
                if (frame.Format != PixelFormat.Yuv420P)
                    throw new UnsupportedVideoException($"A frame came out as {frame.Format}: only 8-bit 4:2:0 is supported so far.");
                if ((frame.Width, frame.Height) != (Video.Width, Video.Height))
                    throw new UnsupportedVideoException($"The frame size changes ({Video.Width}x{Video.Height} to {frame.Width}x{frame.Height}): not supported.");
                var p = frame.Planes;
                var o = frame.Offsets;
                var s = frame.Strides;
                yield return new YuvFrame(p[0].AsMemory(o[0]), s[0], p[1].AsMemory(o[1]), s[1], p[2].AsMemory(o[2]), s[2]) { Pts = frame.Pts };
            }
        }
    }

    (int Width, int Height, long Pts) FirstFrame()
    {
        foreach (var frame in DecodedFrames())
        {
            using (frame)
                return (frame.Width, frame.Height, frame.Pts);
        }

        throw new UnsupportedVideoException("The video track decodes to no frames.");
    }

    /// <summary>
    /// FFmpeg's AVFormatContext.duration in microseconds (update_stream_timings): from the earliest stream start to the
    /// latest stream end, video and audio, as the edit lists present them; null without packets. ShotDetector reports it
    /// as <see cref="DetectedVideo.Duration"/> (through ffprobe's six decimals); with the frame count known it changes no
    /// result.
    /// </summary>
    long? FormatDuration()
    {
        // Only the packets' timing counts here: once the demuxer has read the file's boxes, the packets' data is not read
        // (on a film in the browser that would read gigabytes just to throw them away).
        using var stream = new TimingOnlyStream(_open());
        using var demuxer = new Mp4Demuxer(stream);
        stream.SkipReads = true;
        var spans = new Dictionary<int, (long Start, long End, ManagedCodecs.Rational TimeBase)>();
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.Discard)
                continue;
            ManagedCodecs.Rational timeBase;
            long start = packet.Pts;
            if (demuxer.Video is { } v && packet.StreamIndex == v.StreamIndex)
            {
                timeBase = v.TimeBase;
            }
            else if (demuxer.Audio is { } a && packet.StreamIndex == a.StreamIndex)
            {
                // Presentation starts after the samples the edit list (or an iTunSMPB tag) skips: FFmpeg's start_time.
                timeBase = a.TimeBase;
                long skip = packet.SkipSamples;
                if (a.SampleRate > 0 && a.SampleRate != a.TimeBase.Denominator)
                    skip = skip * a.TimeBase.Denominator / a.SampleRate;
                start += skip;
            }
            else
            {
                continue;
            }

            long end = packet.Pts + packet.Duration;
            spans[packet.StreamIndex] = spans.TryGetValue(packet.StreamIndex, out var span)
                ? (Math.Min(span.Start, start), Math.Max(span.End, end), timeBase)
                : (start, end, timeBase);
        }

        if (spans.Count == 0)
            return null;
        long first = long.MaxValue, last = long.MinValue, longest = long.MinValue;
        foreach (var (start, end, tb) in spans.Values)
        {
            long startUs = Mp4Header.Rescale(start * tb.Numerator, 1_000_000, tb.Denominator);
            long durationUs = Mp4Header.Rescale((end - start) * tb.Numerator, 1_000_000, tb.Denominator);
            first = Math.Min(first, startUs);
            last = Math.Max(last, startUs + durationUs);
            longest = Math.Max(longest, durationUs);
        }

        long duration = Math.Max(longest, last - first);
        return duration > 0 ? duration : null;
    }

    /// <summary>
    /// ffprobe's printed seconds as ShotDetector's file path reads them (3.15.0's VideoReader.Seconds): to the nearest
    /// tick, not TimeSpan.FromSeconds's tick below (4.56 would be 4.5599999).
    /// </summary>
    static TimeSpan Seconds(string text) =>
        TimeSpan.FromTicks((long)Math.Round(double.Parse(text, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond));

    IEnumerable<VideoFrame> DecodedFrames()
    {
        using var stream = _open();
        using var demuxer = new Mp4Demuxer(stream);
        var info = demuxer.Video ?? throw new UnsupportedVideoException("The MP4 file has no video track.");
        var decoder = new H264Decoder(info);
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex != info.StreamIndex)
                continue;
            foreach (var frame in decoder.Decode(packet))
                yield return frame;
        }

        foreach (var frame in decoder.Flush())
            yield return frame;
    }

    /// <summary>
    /// Every sample lasts as long as the others, but the last (its duration is often the track's remainder): ShotDetector
    /// checks the frames' timestamps as they come, this says so before decoding.
    /// </summary>
    static void CheckConstantRate(Mp4Header header)
    {
        var entries = header.TimeToSample;
        int checkedEntries = entries.Count > 1 && entries[^1].Count == 1 ? entries.Count - 1 : entries.Count;
        for (int i = 1; i < checkedEntries; i++)
        {
            if (entries[i].Delta != entries[0].Delta)
                throw new UnsupportedVideoException("The video has a variable frame rate: only a constant frame rate is supported so far.");
        }
    }
}

/// <summary>
/// Reads through to the stream it wraps until <see cref="SkipReads"/> is set; from then on a read reads nothing and
/// reports the bytes as read, leaving the buffer as it was, so a demuxer hands out packets with their timing and without
/// their data (left as allocated).
/// </summary>
sealed class TimingOnlyStream(Stream inner) : Stream
{
    public bool SkipReads { get; set; }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!SkipReads)
            return inner.Read(buffer);
        int n = (int)Math.Clamp(Length - Position, 0, buffer.Length);
        Position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A video this library can't take (yet): the message says what and why.</summary>
public sealed class UnsupportedVideoException(string message) : NotSupportedException(message);
