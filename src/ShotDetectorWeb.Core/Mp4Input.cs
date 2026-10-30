using System.Globalization;
using System.Runtime.CompilerServices;
using ManagedCodecs;
using ManagedCodecs.Containers;
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
/// So far: 8-bit 4:2:0 H.264 (progressive) in an MP4, fragmented or not, at a constant frame rate, not rotated; anything else
/// throws <see cref="UnsupportedVideoException"/> when opened. Damaged files throw ManagedCodecs'
/// <see cref="InvalidDataException"/>.
/// </remarks>
public sealed partial class Mp4Input
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
        using var stream = new TimingOnlyStream(open());
        var header = Mp4Header.Read(stream);
        if (header.SampleEntry is not ("avc1" or "avc3"))
            throw new UnsupportedVideoException($"The video is '{header.SampleEntry}', not H.264 ('avc1'): only H.264 is supported so far.");
        if (header.Transformed)
            throw new UnsupportedVideoException("The video is rotated or flipped (its track matrix): not supported yet.");
        // A fragmented file's samples are in its 'moof' boxes (its 'moov' may have some, or none): they are counted on
        // the walk below.
        if (header.Timescale == 0 || (!header.Fragmented && (header.SampleCount == 0 || header.TotalDuration <= 0)))
            throw new UnsupportedVideoException("The video track has no samples.");
        if (!header.Fragmented)
            CheckConstantRate(header);

        var sps = SpsInfo.FromAvcConfig(header.AvcConfig.Span);
        if (sps.ChromaFormatIdc != 1 || sps.BitDepthLuma != 8 || sps.BitDepthChroma != 8)
        {
            string chroma = sps.ChromaFormatIdc switch { 0 => "4:0:0", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };
            throw new UnsupportedVideoException($"The video is {chroma} at {sps.BitDepthLuma} bits: only 8-bit 4:2:0 is supported so far.");
        }

        if (!sps.FrameMbsOnly)
            throw new UnsupportedVideoException("The video is interlaced (field or MBAFF coding): not supported yet.");

        // One demuxer for the first frame and the format's duration: building its sample index allocates about 77 bytes
        // a sample and keeps 29 (ManagedCodecs 0.1.0), which the browser's heap keeps once it has grown (README, "What the
        // heap holds").
        using var demuxer = new Mp4Demuxer(stream, leaveOpen: true);

        // The first frame's size (after the SPS's cropping) and timestamp: in display order, the smallest pts, which is
        // the stream's start (FFmpeg's start_time for an MP4 video track, edit list applied).
        var (width, height, firstPts) = FirstFrame(demuxer);

        // The audio track as FFmpeg's demuxer describes it: its codec's name from the headers, the rate and channels the
        // demuxer read from the sample entry (an AAC track's from its AudioSpecificConfig). Nothing is decoded.
        if (demuxer.Audio is { } audio)
            Audio = new AudioStream(header.AudioCodec ?? audio.Codec, audio.SampleRate, audio.Channels);

        // Only the packets' timing and sizes count for the duration and the packet list: from here on the packets' data
        // is not read (on a film in the browser that would read gigabytes just to throw them away).
        stream.SkipReads = true;
        var timeBase = new FrameReader.Rational(1, checked((int)header.Timescale));
        var packets = new VideoPackets.Builder(timeBase, (int)Math.Min(header.SampleCount, Array.MaxLength));
        var samples = new SampleDurations();
        long? duration = FormatDuration(demuxer, packets, samples);
        Packets = packets.Build();

        // FFmpeg's average rate is mov.c's nb_frames_for_fps over duration_for_fps: every sample of 'stts' and of each
        // track fragment's 'trun'. In a file without fragments that is the 'stts' totals.
        long sampleCount = header.SampleCount, totalDuration = header.TotalDuration;
        if (header.Fragmented)
        {
            if (samples.Count == 0 || samples.Total <= 0)
                throw new UnsupportedVideoException("The video track has no samples.");
            if (samples.Varies)
                throw new UnsupportedVideoException(VariableRate);
            (sampleCount, totalDuration) = (samples.Count, samples.Total);
        }

        // The decoder's colour tags: the SPS's VUI where it has them, else the container's 'colr' box (FFmpeg's h264
        // decoder overrides the container's values with the SPS's). Full range makes FFmpeg's h264 decoder output yuvj420p.
        bool fullRange = sps.VideoSignalTypePresent ? sps.FullRange : header.ColrFullRange ?? false;
        int? matrix = sps.MatrixCoefficients ?? header.ColrMatrix;
        string colorSpace = matrix is int m && m < MatrixNames.Length ? MatrixNames[m] : "";

        var rate = ManagedCodecs.Rational.Reduce(header.Timescale * sampleCount, totalDuration);
        Video = new YuvVideo
        {
            Width = width,
            Height = height,
            FrameRate = new Fps(rate.Numerator, rate.Denominator),
            PixelFormat = fullRange ? "yuvj420p" : "yuv420p",
            ColorSpace = colorSpace,
            FullRange = fullRange,
            TimeBase = timeBase,
            StartPts = firstPts,
            // FFmpeg's nb_frames is the 'stts' count alone: unknown with an empty 'moov', short of the fragments' with
            // samples in it (ShotDetector takes the length from the duration without it, as OpenCV).
            FrameCount = header.SampleCount > 0 ? header.SampleCount : null,
            Duration = duration is long us
                ? Seconds((us * (1 / 1_000_000.0)).ToString("F6", CultureInfo.InvariantCulture))
                : null,
            Codec = "h264",
            Container = "mov,mp4,m4a,3gp,3g2,mj2",
        };
    }

    /// <summary>The video as FFmpeg's demuxer and decoder report it (average frame rate, time base, start pts, colour tags, frame count).</summary>
    public YuvVideo Video { get; }

    /// <summary>The first audio track as FFmpeg's demuxer describes it (ffprobe's codec_name, sample_rate, channels); null without one.</summary>
    public AudioStream? Audio { get; }

    /// <summary>
    /// How many pictures the decoder decodes at once, each on a thread of its own (ManagedCodecs'
    /// <see cref="H264Decoder.Threads"/>, frame threading): the same frames, in the same order, decoded faster on more
    /// cores. 1, the default, decodes on the thread that reads the frames, which is the only choice where there are no
    /// threads (WebAssembly without them). It applies to the frames read from here on (<see cref="Frames()"/>,
    /// <see cref="FramesAsync"/>, <see cref="DetectAsync"/>, <see cref="ScoreRangeAsync"/>); opening decodes the first
    /// frame on one.
    /// </summary>
    public int DecoderThreads
    {
        get => _decoderThreads;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _decoderThreads = value;
        }
    }

    int _decoderThreads = 1;

    /// <summary>The video track's packets as FFmpeg's demuxer gives them (pts, size, keyframe), from the walk opening made.</summary>
    public VideoPackets Packets { get; }

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
    /// <param name="options">ShotDetector's options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <param name="picture">Measures each frame as it is read (<see cref="PictureMeter"/>), if given.</param>
    public DetectionResult Detect(DetectionOptions? options = null, CancellationToken cancellationToken = default,
        PictureMeter? picture = null)
    {
        options = WithConverter(options);
        return ShotDetection.Detect(Video, Measured(Frames(), picture), options, cancellationToken);
    }

    /// <summary>
    /// Detects shots as <see cref="Detect"/> does, through
    /// <see cref="ShotDetection.DetectAsync(YuvVideo, IAsyncEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/>
    /// on <see cref="FramesAsync"/>: the same result, and between frames the caller's own awaits (in the browser, the
    /// worker's event loop), where it can end the frames early (<paramref name="between"/>) or cancel. Frames that end
    /// early are a file that ends there: the result is the one for the frames read, stats included (ShotDetector 3.16.1
    /// neither warns nor refuses when fewer frames come than <see cref="YuvVideo.FrameCount"/> says).
    /// </summary>
    public Task<DetectionResult> DetectAsync(int every, Func<int, ValueTask<bool>> between,
        DetectionOptions? options = null, CancellationToken cancellationToken = default, PictureMeter? picture = null)
    {
        options = WithConverter(options);
        return ShotDetection.DetectAsync(Video, FramesAsync(every, between, picture: picture), options, cancellationToken);
    }

    /// <summary>
    /// <see cref="Frames"/> as an asynchronous source: after every <paramref name="every"/> frames, before the next one
    /// is decoded, it awaits <paramref name="between"/> with the number of frames given so far; when that returns false,
    /// the frames end there, as if the file did. Each frame given is measured by <paramref name="picture"/>, if any.
    /// </summary>
    public IAsyncEnumerable<YuvFrame> FramesAsync(int every, Func<int, ValueTask<bool>> between,
        CancellationToken cancellationToken = default, PictureMeter? picture = null) =>
        Paused(Measured(Frames(), picture), int.MaxValue, every, between, cancellationToken);

    /// <summary><paramref name="frames"/>, each measured by <paramref name="meter"/> as it is given (none without one).</summary>
    static IEnumerable<YuvFrame> Measured(IEnumerable<YuvFrame> frames, PictureMeter? meter)
    {
        if (meter is null)
            return frames;
        return Each(frames, meter);

        static IEnumerable<YuvFrame> Each(IEnumerable<YuvFrame> frames, PictureMeter meter)
        {
            foreach (var frame in frames)
            {
                meter.Add(frame);
                yield return frame;
            }
        }
    }

    /// <summary>
    /// <paramref name="source"/>'s first <paramref name="limit"/> frames as an asynchronous source, awaiting
    /// <paramref name="between"/> every <paramref name="every"/> frames (<see cref="FramesAsync"/>).
    /// </summary>
    static async IAsyncEnumerable<YuvFrame> Paused(IEnumerable<YuvFrame> source, int limit, int every,
        Func<int, ValueTask<bool>> between, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(every, 1);
        ArgumentNullException.ThrowIfNull(between);
        using var frames = source.GetEnumerator();
        for (int given = 0; given < limit; given++)
        {
            if (given > 0 && given % every == 0 && !await between(given))
                yield break;
            cancellationToken.ThrowIfCancellationRequested();
            if (!frames.MoveNext())
                yield break;
            yield return frames.Current;
        }
    }

    /// <summary>
    /// The decoded frames in display order, each valid until the enumeration moves on. Every enumeration decodes the file
    /// again from its start.
    /// </summary>
    public IEnumerable<YuvFrame> Frames() => Frames(0);

    /// <summary><see cref="Frames()"/> decoded from video packet <paramref name="fromPacket"/> (decode order), a keyframe.</summary>
    IEnumerable<YuvFrame> Frames(int fromPacket)
    {
        foreach (var frame in DecodedFrames(fromPacket))
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

    static (int Width, int Height, long Pts) FirstFrame(Mp4Demuxer demuxer)
    {
        foreach (var frame in DecodedFrames(demuxer))
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
    /// result. Adds each video packet to <paramref name="packets"/> and its duration to <paramref name="samples"/> on the way.
    /// </summary>
    static long? FormatDuration(Mp4Demuxer demuxer, VideoPackets.Builder packets, SampleDurations samples)
    {
        var spans = new Dictionary<int, (long Start, long End, ManagedCodecs.Rational TimeBase)>();
        foreach (var packet in demuxer.ReadPackets())
        {
            if (demuxer.Video is { } video && packet.StreamIndex == video.StreamIndex)
            {
                packets.Add(packet);
                samples.Add(packet.Duration);
            }
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

    /// <summary>
    /// The frames decoded from video packet <paramref name="fromPacket"/> on (decode order; a keyframe, for a decoder that
    /// starts clean there). The packets before it are neither decoded nor read: the demuxer walks them with their data
    /// skipped, as opening does, and reads from the one before it on.
    /// </summary>
    IEnumerable<VideoFrame> DecodedFrames(int fromPacket = 0)
    {
        using var stream = new TimingOnlyStream(_open());
        using var demuxer = new Mp4Demuxer(stream);
        stream.SkipReads = fromPacket > 0;
        foreach (var frame in DecodedFrames(demuxer, fromPacket, stream, _decoderThreads))
            yield return frame;
    }

    static IEnumerable<VideoFrame> DecodedFrames(Mp4Demuxer demuxer, int fromPacket = 0, TimingOnlyStream? skipping = null,
        int threads = 1)
    {
        var info = demuxer.Video ?? throw new UnsupportedVideoException("The MP4 file has no video track.");
        var decoder = new H264Decoder(info) { Threads = threads };
        bool flushed = false;
        try
        {
            foreach (var packet in demuxer.ReadPackets())
            {
                if (packet.StreamIndex != info.StreamIndex)
                    continue;
                if (packet.Sequence < fromPacket)
                {
                    // The demuxer reads a packet's data as it hands out the packet: from the next one on, read them.
                    if (packet.Sequence == fromPacket - 1 && skipping is not null)
                        skipping.SkipReads = false;
                    continue;
                }

                foreach (var frame in decoder.Decode(packet))
                    yield return frame;
            }

            flushed = true;
            foreach (var frame in decoder.Flush())
                yield return frame;
        }
        finally
        {
            // Frames that end early (a stop, a cancel, a range's end, an error) leave pictures being decoded on the
            // decoder's threads. The decoder has no Dispose (ManagedCodecs 0.2.3): flushed, the pictures finish and
            // the threads end now, not after their idle timeout.
            if (!flushed && threads > 1)
                Drain(decoder);
        }
    }

    static void Drain(H264Decoder decoder)
    {
        try
        {
            foreach (var frame in decoder.Flush())
                frame.Dispose();
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException)
        {
            // The pictures still being decoded failed too: nothing is shown of them.
        }
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
                throw new UnsupportedVideoException(VariableRate);
        }
    }

    const string VariableRate = "The video has a variable frame rate: only a constant frame rate is supported so far.";

    /// <summary>
    /// The video packets' count and total duration, and whether any lasts otherwise than the first, the last excepted (as
    /// <see cref="CheckConstantRate"/> excepts the last 'stts' entry): a fragmented file's 'trun' samples, as the walk
    /// gives them.
    /// </summary>
    sealed class SampleDurations
    {
        long _first = -1, _previous = -1;

        public long Count { get; private set; }

        public long Total { get; private set; }

        public bool Varies { get; private set; }

        public void Add(long duration)
        {
            // The packet before this one is not the last: it counts for the check.
            if (_previous >= 0)
            {
                if (_first < 0)
                    _first = _previous;
                else if (_previous != _first)
                    Varies = true;
            }

            _previous = duration;
            Count++;
            Total += duration;
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

/// <summary>
/// An audio track as ffprobe describes it: its codec's FFmpeg name (codec_name: "aac", "mp3", "opus", ...), samples per
/// second and channels. Whether it is measured is <see cref="Mp4Audio"/>'s business: only AAC is.
/// </summary>
public sealed record AudioStream(string Codec, int SampleRate, int Channels);

/// <summary>A video this library can't take (yet): the message says what and why.</summary>
public sealed class UnsupportedVideoException(string message) : NotSupportedException(message);
