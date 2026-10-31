using ManagedCodecs;
using ManagedCodecs.Containers;
using ManagedCodecs.H264;
using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// An H.264 video in an MP4 file (<see cref="VideoInput"/>): described from its boxes (<see cref="Mp4Header"/>, the SPS's
/// <see cref="SpsInfo"/>) and ManagedCodecs' <see cref="Mp4Demuxer"/>, decoded by its <see cref="H264Decoder"/>.
/// </summary>
/// <remarks>
/// So far: 8-bit 4:2:0 H.264 in an MP4, fragmented or not, at a constant frame rate, upright or turned by
/// a quarter turn (<see cref="YuvVideo.Rotation"/>, the frames as decoded): progressive, or
/// interlaced coded as MBAFF frames (the frames as decoded, woven: deinterlacing waits on ShotDetector#614); anything else
/// throws <see cref="UnsupportedVideoException"/> when opened. Damaged files throw ManagedCodecs'
/// <see cref="InvalidDataException"/>.
/// </remarks>
public sealed class Mp4Input : VideoInput
{
    // FFmpeg's names of the H.264 / ISO matrix coefficients (libavutil's av_color_space_name); unspecified (2) is "".
    static readonly string[] MatrixNames =
    [
        "gbr", "bt709", "", "reserved", "fcc", "bt470bg", "smpte170m", "smpte240m", "ycgco", "bt2020nc", "bt2020c",
        "smpte2085", "chroma-derived-nc", "chroma-derived-c", "ictcp", "ipt-c2", "ycgco-re", "ycgco-ro",
    ];

    Mp4Input(Func<Stream> open)
        : base(open)
    {
        using var stream = open();
        var header = Mp4Header.Read(stream);
        if (header.SampleEntry is not ("avc1" or "avc3"))
            throw new UnsupportedVideoException($"The video is '{header.SampleEntry}', not H.264 ('avc1'): only H.264 is supported so far.");
        int rotation = Rotation(header);
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

        // One demuxer for the first frame, the audio's description and the format's duration, which needs both tracks:
        // building its sample index allocates about 77 bytes a sample and keeps 29 (ManagedCodecs 0.1.0), which the
        // browser's heap keeps once it has grown (README, "What the heap holds").
        using var demuxer = new Mp4Demuxer(stream, leaveOpen: true);

        // The first frame's size (after the SPS's cropping) and timestamp: in display order, the smallest pts, which is
        // the stream's start (FFmpeg's start_time for an MP4 video track, edit list applied).
        var (width, height, firstPts) = FirstFrame(demuxer);

        // The audio track as FFmpeg's demuxer describes it: its codec's name from the headers, the rate and channels the
        // demuxer read from the sample entry (an AAC track's from its AudioSpecificConfig). Nothing is decoded.
        if (demuxer.Audio is { } audio)
            Audio = new AudioStream(header.AudioCodec ?? audio.Codec, audio.SampleRate, audio.Channels);

        // Only the packets' timing and sizes count for the duration and the packet list: they come from the sample tables,
        // the packets' data neither read nor allocated (on a film in the browser that would read gigabytes just to throw
        // them away).
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
            Duration = DurationOf(duration),
            Rotation = rotation,
            Codec = "h264",
            Container = "mov,mp4,m4a,3gp,3g2,mj2",
        };
    }

    /// <summary>
    /// The display rotation as ShotDetector's file path takes it from FFmpeg (VideoReader: the display matrix's angle
    /// rounded to a whole degree, its remainder by 360; flips ignored): a quarter turn, or the video is refused, as the file
    /// path refuses it.
    /// </summary>
    static int Rotation(Mp4Header header)
    {
        if (header.Rotation is not double angle)
            return 0;
        if (double.IsNaN(angle))
            throw new UnsupportedVideoException("The video's display matrix has no rotation (it scales a side to nothing): not supported.");
        int rotation = (int)Math.Round(angle) % 360;
        if (rotation % 90 != 0)
            throw new UnsupportedVideoException($"The video is rotated by {rotation} degrees: only quarter turns are supported.");
        return rotation;
    }

    /// <summary>Opens an MP4 file on disk.</summary>
    public static new Mp4Input Open(string path) => new(() => File.OpenRead(path));

    /// <summary>Opens an MP4 file held in memory (in the browser: the file the user picked).</summary>
    public static new Mp4Input Open(byte[] data) => new(() => new MemoryStream(data, writable: false));

    /// <summary>
    /// Opens an MP4 file read through <paramref name="open"/>, which must give a new readable, seekable stream over the
    /// file on every call (in the browser: one reading slices of the picked file, so it never has to fit in memory).
    /// </summary>
    public static new Mp4Input Open(Func<Stream> open) => new(open);

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
        foreach (var packet in demuxer.ReadPacketInfo())
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
    /// The frames decoded from video packet <paramref name="fromPacket"/> on (decode order; a keyframe, for a decoder that
    /// starts clean there). Only the video track is indexed and read (the audio's packets never are), and the packets
    /// before <paramref name="fromPacket"/> are neither decoded nor read: the demuxer walks them with their data skipped,
    /// and reads from the one before it on.
    /// </summary>
    private protected override IEnumerable<VideoFrame> DecodedFrames(int fromPacket)
    {
        using var stream = new TimingOnlyStream(OpenStream());
        using var demuxer = new Mp4Demuxer(stream, MediaTypes.Video, leaveOpen: false);
        stream.SkipReads = fromPacket > 0;
        foreach (var frame in DecodedFrames(demuxer, fromPacket, stream, DecoderThreads))
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
