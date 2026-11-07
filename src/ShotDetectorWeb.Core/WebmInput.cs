using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ManagedCodecs;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// The decoder a host gives <see cref="WebmInput"/> for codecs ManagedCodecs lacks (decision
/// 2026-11-02-webm-browser-decoder.md): in the browser, WebCodecs' <c>VideoDecoder</c>; natively, FFmpeg standing in for
/// it in the checks. It decodes the packets it is given, in order, into frames in display order, one shown frame a
/// packet (WebM's VP9 and AV1: a block is a shown frame), each of the configuration's size and format, packed
/// (<see cref="VideoFrame"/>'s planes as they come; their pts are the input's).
/// </summary>
public interface IHostDecoder
{
    /// <summary>Decodes <paramref name="packets"/> with <paramref name="config"/>.</summary>
    IAsyncEnumerable<VideoFrame> DecodeAsync(HostDecoderConfig config, IEnumerable<EncodedPacket> packets,
        CancellationToken cancellationToken);
}

/// <summary>
/// What a host's decoder is configured with: WebCodecs' codec string, FFmpeg's codec name, the track's CodecPrivate
/// (an 'av1C' for AV1), the frames' size and their format as ManagedCodecs lays it out.
/// </summary>
public sealed record HostDecoderConfig(string Codec, string FfmpegCodec, byte[] Description, int Width, int Height,
    PixelFormat Format);

/// <summary>A packet for a host's decoder: its data, its pts in the video's time base and whether it is a keyframe.</summary>
public readonly record struct EncodedPacket(byte[] Data, long Pts, bool Keyframe);

/// <summary>
/// VP9 or AV1 in a WebM (or Matroska) file (<see cref="VideoInput"/>): described from its headers as FFmpeg's matroska
/// demuxer and its vp9 and libdav1d decoders describe it (<see cref="MatroskaReader"/>, <see cref="Vp9Header"/>,
/// <see cref="Av1Header"/>), its frames decoded by the host's decoder (<see cref="IHostDecoder"/>), each given the pts
/// of its block. So far: 8-bit 4:2:0 and 4:2:2 and 10-bit 4:2:0, with a DefaultDuration (FFmpeg's frame rate) or
/// without (the rate FFmpeg works out from the blocks it probes: <see cref="ProbedFrameRate"/>), with a Segment Duration
/// or without (a live recording's: no duration, as FFmpeg's), progressive, unrotated; anything else is refused when
/// opened.
/// </summary>
public sealed class WebmInput : VideoInput
{
    readonly IHostDecoder _decoder;
    readonly MatroskaReader _matroska;
    readonly HostDecoderConfig _config;
    readonly bool _vp9;

    WebmInput(Func<Stream> open, IHostDecoder decoder)
        : base(open)
    {
        _decoder = decoder;
        using var stream = open();
        var mkv = _matroska = MatroskaReader.Read(stream);
        var track = mkv.Video;
        string ffmpegCodec = track.CodecId switch
        {
            "V_VP9" => "vp9",
            "V_AV1" => "av1",
            "V_VP8" => throw new UnsupportedVideoException($"The video is VP8 (FFmpeg's vp8; 'V_VP8'): {Scope}"),
            "V_MPEG4/ISO/AVC" => throw new UnsupportedVideoException($"The video is H.264 in Matroska (FFmpeg's h264; 'V_MPEG4/ISO/AVC'): {Scope}"),
            "V_MPEGH/ISO/HEVC" => throw new UnsupportedVideoException($"The video is HEVC (H.265, FFmpeg's hevc; 'V_MPEGH/ISO/HEVC'): {Scope}"),
            _ => throw new UnsupportedVideoException($"The video is '{track.CodecId}': {Scope}"),
        };
        _vp9 = ffmpegCodec == "vp9";
        if (track.HasProjection)
            throw new UnsupportedVideoException("The video is rotated (a Matroska Projection): not supported yet in WebM.");
        if (track.Interlaced == 1)
            throw new UnsupportedVideoException("The video is interlaced: not supported in WebM.");
        long? frameDuration = track.DefaultDuration is long d && d > 0 ? d : null;

        // The blocks' timing, sizes and keyframes (FFmpeg's packets), their data not read.
        var timeBase = Reduce(mkv.TimestampScale, 1_000_000_000);
        var packets = new VideoPackets.Builder(timeBase, 1024);
        MatroskaReader.VideoBlock? firstKey = null;
        long firstPts = long.MaxValue, last = long.MinValue;
        foreach (var block in mkv.Blocks(stream))
        {
            if (block.Timestamp <= last)
                throw new UnsupportedVideoException("The WebM file's video blocks aren't in presentation order: not supported.");
            last = block.Timestamp;
            packets.Add(block.Timestamp, block.Size, block.Keyframe);
            firstPts = Math.Min(firstPts, block.Timestamp);
            if (block.Keyframe && firstKey is null)
                firstKey = block;
        }

        Packets = packets.Build();
        if (Packets.Count == 0 || firstKey is not { } key)
            throw new UnsupportedVideoException("The video track has no frames.");

        // The pictures as the decoder FFmpeg probes the stream with says them: VP9's key frame header, AV1's sequence
        // header (the track's 'av1C').
        var header = (_vp9 ? Vp9Header.Read(MatroskaReader.ReadData(stream, key)) : Av1Header.Read(track.CodecPrivate))
            ?? throw new UnsupportedVideoException($"The {ffmpegCodec.ToUpperInvariant()} stream's header couldn't be read.");
        string pixelFormat = header.PixelFormat
            ?? throw new UnsupportedVideoException($"The video is {header.Describe()}: only 8-bit 4:2:0 and 4:2:2, and 10-bit 4:2:0, are supported so far.");

        // FFmpeg's matroska demuxer: the average rate from DefaultDuration (av_reduce of 1e9 / DefaultDuration, at most
        // 30000), the format's duration from the Segment's Duration (in microseconds, a double truncated). Without a
        // DefaultDuration, the rate FFmpeg works out from the blocks it probes (decision
        // 2026-11-05-webm-without-default-duration.md): the average, or r_frame_rate when it has none, as the file path
        // reports it.
        Fps fps;
        if (frameDuration is long defaultDuration)
        {
            var rate = ReduceRate(1_000_000_000, defaultDuration, 30000);
            fps = new Fps(rate.Num, rate.Den);
        }
        else
        {
            var probed = new List<ProbedFrameRate.ProbedPacket>();
            foreach (var p in mkv.Packets(stream))
            {
                // A BlockGroup's BlockDuration gives FFmpeg packet durations, which it averages instead: not ported.
                if (p.Video && p.HasDuration)
                    throw new UnsupportedVideoException("The WebM file's video track has no DefaultDuration but its blocks have durations: not supported yet.");
                probed.Add(new ProbedFrameRate.ProbedPacket(p.Video, p.Timestamp, p.Size));
            }

            var rates = ProbedFrameRate.Estimate(probed, timeBase.Num, timeBase.Den);
            var chosen = rates.AverageFrameRate ?? rates.RFrameRate;
            fps = new Fps(checked((int)chosen.Num), checked((int)chosen.Den));
        }
        // Without one (a live recording's, written as it went), FFmpeg's format has no duration either.
        long? durationUs = mkv.Duration is double ticks && ticks > 0 ? (long)(ticks * mkv.TimestampScale * 1000 / 1_000_000) : null;

        Video = new YuvVideo
        {
            Width = track.Width,
            Height = track.Height,
            FrameRate = fps,
            PixelFormat = pixelFormat,
            ColorSpace = header.Matrix is int m ? MatrixName(m) ?? "" : "",
            FullRange = header.FullRange,
            // libdav1d's chroma_sample_location for 4:2:0 (vertical: left, colocated: topleft); FFmpeg's vp9 gives none.
            ChromaLocation = header.ChromaLocation switch { 1 => "left", 2 => "topleft", _ => null },
            TimeBase = timeBase,
            StartPts = firstPts,
            FrameCount = null,
            Duration = DurationOf(durationUs),
            Rotation = 0,
            FieldOrder = FrameReader.FieldOrder.Progressive,
            FrameTimestamps = FrameTimestampsOf(Packets),
            Codec = ffmpegCodec,
            Container = "matroska,webm",
        };

        if (mkv.Audio is { } audio)
            Audio = new AudioStream(AudioCodec(audio.CodecId), audio.CodecId == "A_OPUS" ? 48000 : (int)audio.SamplingFrequency, audio.Channels);

        _config = new HostDecoderConfig(HostCodecString(ffmpegCodec, header, track.CodecPrivate), ffmpegCodec, track.CodecPrivate,
            track.Width, track.Height, DecodedFormat(pixelFormat));
    }

    const string Scope = Refusal.Scope;

    /// <summary>Opens a WebM or Matroska file read through <paramref name="open"/>, its frames decoded by <paramref name="decoder"/>.</summary>
    public static new WebmInput Open(Func<Stream> open, IHostDecoder decoder) => new(open, decoder);

    /// <inheritdoc/>
    public override bool DecodedByHost => true;

    /// <summary>What the host's decoder is configured with (its codec string for WebCodecs among it).</summary>
    public HostDecoderConfig DecoderConfig => _config;

    /// <summary>Not used: the host's decoder answers asynchronously (<see cref="DecodedFramesAsync"/>), its frames typed by the input.</summary>
    private protected override IEnumerable<VideoFrame> DecodedFrames(int fromPacket) =>
        throw new NotSupportedException("A WebM's frames come from the host's decoder, asynchronously.");

    /// <summary>
    /// The host decoder's frames from packet <paramref name="fromPacket"/> on (a keyframe), each given its block's pts, and
    /// its picture type as FFmpeg's decoder says it: VP9's from its frame header (a key or intra-only frame I, else P),
    /// AV1's from its block (a keyframe I, else P).
    /// </summary>
    private protected override async IAsyncEnumerable<(VideoFrame Frame, PictureType Type)> DecodedFramesAsync(int fromPacket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = OpenStream();
        await foreach (var item in HostFramesAsync(_decoder, _config, types => Read(stream, fromPacket, types), fromPacket, cancellationToken))
            yield return item;
    }

    // The blocks from fromPacket on, each read as the decoder asks for it, its picture type queued.
    IEnumerable<EncodedPacket> Read(Stream stream, int fromPacket, ConcurrentQueue<PictureType> types)
    {
        int index = 0;
        foreach (var block in _matroska.Blocks(stream))
        {
            if (index++ < fromPacket)
                continue;
            var data = MatroskaReader.ReadData(stream, block);
            bool intra = _vp9 ? Vp9Header.ShownIsIntra(data) : block.Keyframe;
            types.Enqueue(intra ? PictureType.I : PictureType.P);
            yield return new EncodedPacket(data, block.Timestamp, block.Keyframe);
        }
    }

    // ffprobe's codec_name of a Matroska audio track's CodecID.
    static string AudioCodec(string codecId) => codecId switch
    {
        "A_OPUS" => "opus",
        "A_VORBIS" => "vorbis",
        "A_AAC" or "A_AAC/MPEG4/LC" or "A_AAC/MPEG2/LC" => "aac",
        "A_MPEG/L3" => "mp3",
        "A_MPEG/L2" => "mp2",
        "A_FLAC" => "flac",
        "A_AC3" => "ac3",
        "A_EAC3" => "eac3",
        "A_PCM/INT/LIT" => "pcm_s16le",
        _ => codecId,
    };

    static FrameReader.Rational Reduce(long num, long den)
    {
        long g = (long)System.Numerics.BigInteger.GreatestCommonDivisor(num, den);
        return new FrameReader.Rational(checked((int)(num / g)), checked((int)(den / g)));
    }

    /// <summary>libavutil's av_reduce of <paramref name="num"/>/<paramref name="den"/> with terms at most <paramref name="max"/>.</summary>
    static (int Num, int Den) ReduceRate(long num, long den, long max)
    {
        long g = (long)System.Numerics.BigInteger.GreatestCommonDivisor(num, den);
        num /= g;
        den /= g;
        if (num <= max && den <= max)
            return ((int)num, (int)den);

        // The continued fraction's best approximation with terms at most max (av_reduce's loop).
        long a0n = 0, a0d = 1, a1n = 1, a1d = 0;
        while (den != 0)
        {
            long x = num / den, next = num - den * x;
            long a2n = x * a1n + a0n, a2d = x * a1d + a0d;
            if (a2n > max || a2d > max)
            {
                if (a1n != 0)
                    x = (max - a0n) / a1n;
                if (a1d != 0)
                    x = Math.Min(x, (max - a0d) / a1d);
                if (den * (2 * x * a1d + a0d) > num * a1d)
                    (a1n, a1d) = (x * a1n + a0n, x * a1d + a0d);
                break;
            }

            (a0n, a0d, a1n, a1d) = (a1n, a1d, a2n, a2d);
            (num, den) = (den, next);
        }

        return ((int)a1n, (int)a1d);
    }
}
