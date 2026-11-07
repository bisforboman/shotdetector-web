using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ManagedCodecs;
using ManagedCodecs.Containers;
using ManagedCodecs.H264;
using ShotDetector;
using ShotDetector.FastYuv;

namespace ShotDetectorWeb;

/// <summary>
/// An H.264 video in an MP4 file (<see cref="VideoInput"/>): described from its boxes (<see cref="Mp4Header"/>, the SPS's
/// <see cref="SpsInfo"/>) and ManagedCodecs' <see cref="Mp4Demuxer"/>, decoded by its <see cref="H264Decoder"/>; or VP9 or
/// AV1 in an MP4 ('vp09', 'av01': YouTube's), described as FFmpeg's mov demuxer and its vp9 and libdav1d decoders describe
/// it (the MP4's rate, duration and rotation as H.264's; the picture from VP9's key frame header or the 'av1C', as a
/// WebM's), its frames decoded by the host's decoder (<see cref="IHostDecoder"/>, the browser's; decision
/// 2026-11-02-webm-browser-decoder.md), each given its packet's pts.
/// </summary>
/// <remarks>
/// So far: 8-bit 4:2:0 or 4:2:2 and 10-bit 4:2:0 H.264 (High, High 4:2:2, High 10) in an MP4, fragmented or not, at a constant or variable frame rate (every frame's pts given
/// up front, <see cref="YuvVideo.FrameTimestamps"/>), upright or turned by
/// a quarter turn (<see cref="YuvVideo.Rotation"/>, the frames as decoded): progressive, or
/// interlaced coded as MBAFF frames (its field order and each frame's flags described, for ShotDetector's
/// <see cref="DetectionOptions.Deinterlace"/>; frames coded as field pictures refused); VP9 and AV1 as a WebM's are
/// (8-bit 4:2:0 and 4:2:2, 10-bit 4:2:0, progressive), at any of those rates and turns, their packets in presentation
/// order and none an edit list leaves out; anything else
/// throws <see cref="UnsupportedVideoException"/> when opened. Damaged files throw ManagedCodecs'
/// <see cref="InvalidDataException"/>.
/// </remarks>
public sealed class Mp4Input : VideoInput
{
    readonly IHostDecoder? _decoder;
    readonly HostDecoderConfig? _config;

    Mp4Input(Func<Stream> open, IHostDecoder? decoder)
        : base(open)
    {
        using var stream = open();
        var header = Mp4Header.Read(stream);
        // VP9 and AV1 go to the host's decoder (the browser's), as a WebM's.
        string? hosted = header.SampleEntry switch { "vp09" => "vp9", "av01" => "av1", _ => null };
        if (hosted is null && header.SampleEntry is not ("avc1" or "avc3"))
            throw new UnsupportedVideoException($"The video is {Refusal.Mp4Codec(header.SampleEntry)}, not H.264: {Refusal.Scope}");
        if (hosted is not null && decoder is null)
            throw new UnsupportedVideoException($"The video is {Refusal.Mp4Codec(header.SampleEntry)}: its frames need the browser's decoder, which isn't given here.");
        _decoder = hosted is null ? null : decoder;
        int rotation = Rotation(header);
        // A fragmented file's samples are in its 'moof' boxes (its 'moov' may have some, or none): they are counted on
        // the walk below.
        if (header.Timescale == 0 || (!header.Fragmented && (header.SampleCount == 0 || header.TotalDuration <= 0)))
            throw new UnsupportedVideoException("The video track has no samples.");

        var sps = hosted is null ? SpsInfo.FromAvcConfig(header.AvcConfig.Span) : null;
        // The formats ShotDetector's frame source takes (3.24.0): 8-bit 4:2:0 and 4:2:2, 10-bit 4:2:0.
        bool supported = sps is null || (sps.BitDepthLuma == sps.BitDepthChroma &&
            (sps.ChromaFormatIdc, sps.BitDepthLuma) is (1, 8) or (2, 8) or (1, 10));
        if (!supported && sps is not null)
        {
            string chroma = sps.ChromaFormatIdc switch { 0 => "4:0:0 (greyscale)", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };
            string depth = sps.BitDepthLuma == sps.BitDepthChroma ? $"{sps.BitDepthLuma} bits" : $"{sps.BitDepthLuma} bits (luma), {sps.BitDepthChroma} (chroma)";
            throw new UnsupportedVideoException($"The video is {chroma} at {depth}: only 8-bit 4:2:0 and 4:2:2, and 10-bit 4:2:0, are supported so far.");
        }

        // One demuxer for the first frame, the audio's description and the format's duration, which needs both tracks:
        // building its sample index allocates about 77 bytes a sample and keeps 29 (ManagedCodecs 0.1.0), which the
        // browser's heap keeps once it has grown (README, "What the heap holds").
        using var demuxer = new Mp4Demuxer(stream, leaveOpen: true);

        // The first frame's size (after the SPS's cropping) and timestamp: in display order, the smallest pts, which is
        // the stream's start (FFmpeg's start_time for an MP4 video track, edit list applied).
        var (width, height, firstPts, fieldOrder) = sps is null
            ? (demuxer.Video?.Width ?? 0, demuxer.Video?.Height ?? 0, 0L, FrameReader.FieldOrder.Progressive)
            : FirstFrame(demuxer);

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
        CodecHeader? picture = null;
        if (hosted is not null)
            (picture, firstPts) = HostedPicture(hosted, header, open);

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
        // decoder overrides the container's values with the SPS's). Full range makes FFmpeg's h264 decoder output
        // yuvj420p or yuvj422p at 8 bits; at 10 bits there is no such format, only the range tag.
        // VP9's and AV1's decoders give each frame the bitstream's, whatever the container says (as a WebM's).
        bool fullRange = picture?.FullRange ?? (sps!.VideoSignalTypePresent ? sps.FullRange : header.ColrFullRange ?? false);
        int? matrix = picture is not null ? picture.Matrix : sps!.MatrixCoefficients ?? header.ColrMatrix;
        string colorSpace = matrix is int m ? MatrixName(m) ?? "" : "";

        var rate = ManagedCodecs.Rational.Reduce(header.Timescale * sampleCount, totalDuration);
        var fps = new Fps(rate.Numerator, rate.Denominator);
        Video = new YuvVideo
        {
            Width = width,
            Height = height,
            FrameRate = fps,
            PixelFormat = picture is not null
                ? picture.PixelFormat ?? throw new UnsupportedVideoException($"The video is {picture.Describe()}: only 8-bit 4:2:0 and 4:2:2, and 10-bit 4:2:0, are supported so far.")
                : (sps!.ChromaFormatIdc, sps.BitDepthLuma) switch
            {
                (1, 10) => "yuv420p10le",
                (2, _) => fullRange ? "yuvj422p" : "yuv422p",
                _ => fullRange ? "yuvj420p" : "yuv420p",
            },
            ColorSpace = colorSpace,
            FullRange = fullRange,
            // The SPS's chroma location (FFmpeg's h264 decoder gives each frame the SPS's; no MP4 box carries one); libdav1d's
            // for AV1 4:2:0 (vertical: left, colocated: topleft), FFmpeg's vp9 none.
            ChromaLocation = picture is not null
                ? picture.ChromaLocation switch { 1 => "left", 2 => "topleft", _ => null }
                : ChromaLocationName(sps!.ChromaLocation),
            TimeBase = timeBase,
            StartPts = firstPts,
            // FFmpeg's nb_frames is the 'stts' count alone: unknown with an empty 'moov', short of the fragments' with
            // samples in it (ShotDetector takes the length from the duration without it, as OpenCV).
            FrameCount = header.SampleCount > 0 ? header.SampleCount : null,
            Duration = DurationOf(duration),
            Rotation = rotation,
            FieldOrder = fieldOrder,
            FrameTimestamps = FrameTimestampsOf(Packets),
            Codec = hosted ?? "h264",
            Container = "mov,mp4,m4a,3gp,3g2,mj2",
        };

        if (picture is not null)
            _config = new HostDecoderConfig(HostCodecString(hosted!, picture, header.Av1Config.Span), hosted!,
                header.Av1Config.ToArray(), width, height, DecodedFormat(Video.PixelFormat));
    }

    /// <summary>
    /// A VP9 or AV1 track's picture as FFmpeg's decoders say it (VP9's first key frame's header, AV1's sequence header in
    /// the 'av1C') and its first frame's pts, its packets' least (one shown frame a packet, in presentation order). Packets an
    /// edit list leaves out (which the decoder would decode for their references and not show) are refused.
    /// </summary>
    (CodecHeader Picture, long FirstPts) HostedPicture(string codec, Mp4Header header, Func<Stream> open)
    {
        var pts = Packets.Pts;
        var flags = Packets.Flags;
        int key = -1;
        for (int i = 0; i < pts.Length; i++)
        {
            if ((flags[i] & PacketFlags.Discard) != 0)
                throw new UnsupportedVideoException($"An edit list leaves out some of the {codec.ToUpperInvariant()} video's frames: not supported.");
            if (i > 0 && pts[i] <= pts[i - 1])
                throw new UnsupportedVideoException($"The {codec.ToUpperInvariant()} video's packets aren't in presentation order: not supported.");
            if (key < 0 && (flags[i] & PacketFlags.Keyframe) != 0)
                key = i;
        }

        if (pts.Length == 0 || key < 0)
            throw new UnsupportedVideoException("The video track has no frames.");
        CodecHeader? picture;
        if (codec == "vp9")
        {
            using var stream = open();
            using var demuxer = new Mp4Demuxer(stream, MediaTypes.Video, leaveOpen: false);
            var first = demuxer.ReadPackets().First(p => p.Sequence == key);
            picture = Vp9Header.Read(first.Data.Span);
        }
        else
        {
            picture = Av1Header.Read(header.Av1Config.Span);
        }

        return (picture ?? throw new UnsupportedVideoException($"The {codec.ToUpperInvariant()} stream's header couldn't be read."), pts[0]);
    }

    /// <inheritdoc/>
    public override bool DecodedByHost => _decoder is not null;

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
    public static new Mp4Input Open(string path) => new(() => File.OpenRead(path), null);

    /// <summary>Opens an MP4 file held in memory (in the browser: the file the user picked).</summary>
    public static new Mp4Input Open(byte[] data) => new(() => new MemoryStream(data, writable: false), null);

    /// <summary>
    /// Opens an MP4 file read through <paramref name="open"/>, which must give a new readable, seekable stream over the
    /// file on every call (in the browser: one reading slices of the picked file, so it never has to fit in memory).
    /// </summary>
    public static new Mp4Input Open(Func<Stream> open) => new(open, null);

    /// <summary>
    /// <see cref="Open(Func{Stream})"/>, and VP9 or AV1 in the MP4 decoded by <paramref name="decoder"/> (the browser's);
    /// without one, those are refused.
    /// </summary>
    public static new Mp4Input Open(Func<Stream> open, IHostDecoder? decoder) => new(open, decoder);

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
    /// Each stream's end is its start plus FFmpeg's stream duration (ManagedCodecs' <see cref="VideoStreamInfo.Duration"/>
    /// and <see cref="AudioStreamInfo.Duration"/>: the edits' summed length, else 'mdhd''s, else the samples', a
    /// fragmented file's 'trun' ones); the video's not the last shown
    /// packet's end, which can be later (at a variable rate a packet lasts its decode-order delta, not its frame's) or
    /// earlier (an edit list from media_time 0 before a composition offset discards the last packets inside it).
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
        // Each stream ends where its stream's duration says, from its start (above): the video from its first shown frame,
        // the audio after the samples skipped. An audio track's 'mdhd' can be shorter than its packets (an Android
        // phone's: 196605 of 192 frames' 196608), which FFmpeg's end follows.
        if (demuxer.Video is { } shown && spans.TryGetValue(shown.StreamIndex, out var videoSpan) && shown.Duration > 0)
            spans[shown.StreamIndex] = videoSpan with { End = videoSpan.Start + shown.Duration };
        if (demuxer.Audio is { } heard && spans.TryGetValue(heard.StreamIndex, out var audioSpan) && heard.Duration > 0)
            spans[heard.StreamIndex] = audioSpan with { End = audioSpan.Start + heard.Duration };

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
        if (_decoder is not null)
            throw new NotSupportedException("A VP9 or AV1 video's frames come from the host's decoder, asynchronously.");
        using var stream = new TimingOnlyStream(OpenStream());
        using var demuxer = new Mp4Demuxer(stream, MediaTypes.Video, leaveOpen: false);
        stream.SkipReads = fromPacket > 0;
        foreach (var frame in DecodedFrames(demuxer, fromPacket, stream, DecoderThreads))
            yield return frame;
    }

    /// <summary>
    /// The frames from packet <paramref name="fromPacket"/> on: H.264's as <see cref="DecodedFrames(int)"/> gives them; VP9's
    /// and AV1's from the host's decoder, each given its packet's pts, and its picture type as FFmpeg's decoder says it
    /// (VP9's from its frame header, AV1's from its packet: a keyframe I, else P).
    /// </summary>
    private protected override IAsyncEnumerable<(VideoFrame Frame, PictureType Type)> DecodedFramesAsync(int fromPacket,
        CancellationToken cancellationToken) =>
        _decoder is null
            ? base.DecodedFramesAsync(fromPacket, cancellationToken)
            : HostFramesAsync(_decoder, _config!, types => HostPackets(fromPacket, types), fromPacket, cancellationToken);

    // The video's packets from fromPacket on, each read as the decoder asks for it (those before it walked with their data
    // skipped), its picture type queued.
    IEnumerable<EncodedPacket> HostPackets(int fromPacket, ConcurrentQueue<PictureType> types)
    {
        using var stream = new TimingOnlyStream(OpenStream());
        using var demuxer = new Mp4Demuxer(stream, MediaTypes.Video, leaveOpen: false);
        stream.SkipReads = fromPacket > 0;
        bool vp9 = Video.Codec == "vp9";
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.Sequence < fromPacket)
            {
                if (packet.Sequence == fromPacket - 1)
                    stream.SkipReads = false;
                continue;
            }

            var data = packet.Data.ToArray();
            types.Enqueue((vp9 ? Vp9Header.ShownIsIntra(data) : packet.IsKeyframe) ? PictureType.I : PictureType.P);
            yield return new EncodedPacket(data, packet.Pts, packet.IsKeyframe);
        }
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
