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
/// So far: 8-bit 4:2:0 H.264 in an MP4, fragmented or not, at a constant or variable frame rate (every frame's pts given
/// up front, <see cref="YuvVideo.FrameTimestamps"/>, where the rate varies), upright or turned by
/// a quarter turn (<see cref="YuvVideo.Rotation"/>, the frames as decoded): progressive, or
/// interlaced coded as MBAFF frames (its field order and each frame's flags described, for ShotDetector's
/// <see cref="DetectionOptions.Deinterlace"/>; frames coded as field pictures refused); anything else
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
        var (width, height, firstPts, fieldOrder) = FirstFrame(demuxer);

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
        long? duration = FormatDuration(demuxer, packets, samples, header.Fragmented ? null : header.TotalDuration);
        Packets = packets.Build();

        // FFmpeg's average rate is mov.c's nb_frames_for_fps over duration_for_fps: every sample of 'stts' and of each
        // track fragment's 'trun'. In a file without fragments that is the 'stts' totals.
        long sampleCount = header.SampleCount, totalDuration = header.TotalDuration;
        if (header.Fragmented)
        {
            if (samples.Count == 0 || samples.Total <= 0)
                throw new UnsupportedVideoException("The video track has no samples.");
            (sampleCount, totalDuration) = (samples.Count, samples.Total);
        }

        // The decoder's colour tags: the SPS's VUI where it has them, else the container's 'colr' box (FFmpeg's h264
        // decoder overrides the container's values with the SPS's). Full range makes FFmpeg's h264 decoder output yuvj420p.
        bool fullRange = sps.VideoSignalTypePresent ? sps.FullRange : header.ColrFullRange ?? false;
        int? matrix = sps.MatrixCoefficients ?? header.ColrMatrix;
        string colorSpace = matrix is int m && m < MatrixNames.Length ? MatrixNames[m] : "";

        var rate = ManagedCodecs.Rational.Reduce(header.Timescale * sampleCount, totalDuration);
        var fps = new Fps(rate.Numerator, rate.Denominator);
        Video = new YuvVideo
        {
            Width = width,
            Height = height,
            FrameRate = fps,
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
            FieldOrder = fieldOrder,
            FrameTimestamps = VariableRate(Packets, firstPts, timeBase, fps),
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

    /// <summary>
    /// The first frame's size and pts, and the stream's field order as ffprobe prints it (ManagedCodecs'
    /// <see cref="H264Decoder.FieldOrder"/>: FFmpeg's parser's for the first picture decoded), which ShotDetector's
    /// <see cref="DeinterlaceMode.Auto"/> goes by. A first frame coded as field pictures is refused.
    /// </summary>
    static (int Width, int Height, long Pts, FrameReader.FieldOrder FieldOrder) FirstFrame(Mp4Demuxer demuxer)
    {
        H264Decoder? decoder = null;
        foreach (var frame in DecodedFrames(demuxer, created: d => decoder = d))
        {
            using (frame)
            {
                if (frame.IsCodedAsFields)
                    throw new UnsupportedVideoException(FieldPictures);
                return (frame.Width, frame.Height, frame.Pts, FieldOrderOf(decoder!.FieldOrder));
            }
        }

        throw new UnsupportedVideoException("The video track decodes to no frames.");
    }

    /// <summary>
    /// FFmpeg's AVFormatContext.duration in microseconds (update_stream_timings): from the earliest stream start to the
    /// latest stream end, video and audio, as the edit lists present them; null without packets. ShotDetector reports it
    /// as <see cref="DetectedVideo.Duration"/> (through ffprobe's six decimals); with the frame count known it changes no
    /// result. Adds each video packet to <paramref name="packets"/> and its duration to <paramref name="samples"/> on the way.
    /// The video's end is its start plus FFmpeg's stream duration: the samples' total (<paramref name="videoTotal"/>, the
    /// 'stts' one; else the walk's, a fragmented file's 'trun' samples, its track_end), cut where the edit list ends
    /// (mov.c's mov_read_stts and mov_fix_index: the least of the two). The last shown packet's end can be later: at a
    /// variable rate, a packet lasts its decode-order delta, not its frame's.
    /// </summary>
    static long? FormatDuration(Mp4Demuxer demuxer, VideoPackets.Builder packets, SampleDurations samples, long? videoTotal)
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
        if (demuxer.Video is { } shown && spans.TryGetValue(shown.StreamIndex, out var videoSpan))
        {
            long total = videoTotal ?? samples.Total;
            if (total > 0 && videoSpan.Start + total < videoSpan.End)
                spans[shown.StreamIndex] = videoSpan with { End = videoSpan.Start + total };
        }

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

    /// <summary>FFmpeg's field order (ManagedCodecs' has AVFieldOrder's values) as FrameReader's, which ShotDetector takes.</summary>
    static FrameReader.FieldOrder FieldOrderOf(ManagedCodecs.FieldOrder order) => order switch
    {
        ManagedCodecs.FieldOrder.Progressive => FrameReader.FieldOrder.Progressive,
        ManagedCodecs.FieldOrder.TT => FrameReader.FieldOrder.TopFirst,
        ManagedCodecs.FieldOrder.BB => FrameReader.FieldOrder.BottomFirst,
        ManagedCodecs.FieldOrder.TB => FrameReader.FieldOrder.TopCodedBottomFirst,
        ManagedCodecs.FieldOrder.BT => FrameReader.FieldOrder.BottomCodedTopFirst,
        _ => FrameReader.FieldOrder.Unknown,
    };

    static IEnumerable<VideoFrame> DecodedFrames(Mp4Demuxer demuxer, int fromPacket = 0, TimingOnlyStream? skipping = null,
        int threads = 1, Action<H264Decoder>? created = null)
    {
        var info = demuxer.Video ?? throw new UnsupportedVideoException("The MP4 file has no video track.");
        var decoder = new H264Decoder(info) { Threads = threads };
        created?.Invoke(decoder);
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
    /// Every shown frame's pts in presentation order (the packets' that aren't discarded, sorted: what FFmpeg labels the
    /// frames with) when the frames don't come at the average rate, for ShotDetector's
    /// <see cref="YuvVideo.FrameTimestamps"/> (a variable frame rate, which it then takes as the file path does); null
    /// when every frame is within half a frame of where the average rate puts it from the first, the constant-rate check
    /// ShotDetector makes without them (VideoReader.CheckConstantRate), so a constant-rate video is described as before.
    /// </summary>
    static long[]? VariableRate(VideoPackets packets, long firstPts, FrameReader.Rational timeBase, Fps rate)
    {
        var pts = new List<long>(packets.Count);
        for (int i = 0; i < packets.Count; i++)
        {
            if ((packets.Flags[i] & PacketFlags.Discard) == 0)
                pts.Add(packets.Pts[i]);
        }

        pts.Sort();
        double seconds = timeBase.Num / (double)timeBase.Den, perSecond = rate.Value;
        for (int k = 0; k < pts.Count; k++)
        {
            double from = (pts[k] - firstPts) * seconds, expected = k / perSecond;
            if (!(Math.Abs(from - expected) <= 0.5 / perSecond))
                return [.. pts];
        }

        return null;
    }

    /// <summary>The video packets' count and total duration: a fragmented file's 'trun' samples, as the walk gives them.</summary>
    sealed class SampleDurations
    {
        public long Count { get; private set; }

        public long Total { get; private set; }

        public void Add(long duration)
        {
            Count++;
            Total += duration;
        }
    }
}
