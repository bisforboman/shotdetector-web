using ManagedCodecs;
using ManagedCodecs.Containers;
using ManagedCodecs.Mjpeg;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// An MJPEG video in an AVI file (<see cref="VideoInput"/>): described from its headers (<see cref="AviHeader"/>) and
/// ManagedCodecs' <see cref="AviDemuxer"/>, decoded by its <see cref="MjpegDecoder"/>, which is bit-exact with FFmpeg's
/// (decision 2026-10-30-mjpeg-avi.md).
/// </summary>
/// <remarks>
/// So far: baseline or extended Huffman JPEG at 4:2:0 (FFmpeg's yuvj420p), every frame a chunk of its own; anything else
/// throws <see cref="UnsupportedVideoException"/> when opened: 4:2:2 and 4:4:4 JPEG (ShotDetector's frame source takes
/// 4:2:0 only), and a file whose video chunks are fewer than its header says (a dropped frame, or an OpenDML file over
/// 1 GB, whose later parts ManagedCodecs doesn't read). Audio streams are described, not decoded.
/// </remarks>
public sealed class AviInput : VideoInput
{
    AviInput(Func<Stream> open)
        : base(open)
    {
        using var stream = new TimingOnlyStream(open()) { ReadsThrough = ChunkHeader };
        var header = AviHeader.Read(stream);
        stream.Position = 0;
        using var demuxer = new AviDemuxer(stream, leaveOpen: true);
        var info = demuxer.Video ?? throw new UnsupportedVideoException("The AVI file has no video stream.");
        if (info.Codec != CodecIds.Mjpeg)
            throw new UnsupportedVideoException($"The video is '{info.Codec}', not MJPEG: only MJPEG is supported in AVI so far.");
        var strh = header.Video ?? throw new UnsupportedVideoException("The AVI file has no video stream.");

        // The first frame's size and format: a JPEG's chroma subsampling is its own, frame by frame.
        var (width, height, format) = FirstFrame(demuxer, info);
        if (format != PixelFormat.Yuv420P)
        {
            string chroma = format switch
            {
                PixelFormat.Yuv422P => "4:2:2",
                PixelFormat.Yuv444P => "4:4:4",
                PixelFormat.Gray8 => "greyscale",
                _ => format.ToString(),
            };
            throw new UnsupportedVideoException($"The video is {chroma} JPEG: only 4:2:0 is supported so far (ShotDetector takes 4:2:0 frames only).");
        }

        // Every chunk's timing and size, its data not read.
        stream.SkipReads = true;
        var timeBase = Rational.Reduce(info.TimeBase.Numerator, info.TimeBase.Denominator);
        var packets = new VideoPackets.Builder(new FrameReader.Rational(checked((int)timeBase.Numerator), checked((int)timeBase.Denominator)),
            (int)Math.Min(strh.Length, Array.MaxLength));
        long firstPts = -1;
        int count = 0;
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex != info.StreamIndex)
                continue;
            packets.Add(packet);
            if (firstPts < 0)
                firstPts = packet.Pts;
            count++;
        }

        Packets = packets.Build();

        // FFmpeg counts an empty video chunk as a frame (a dropped one: the next pts is one more), ManagedCodecs doesn't
        // (ManagedCodecs#226), and ShotDetector's frame source takes no gaps: such a file is refused, as is an OpenDML file
        // whose later parts ('AVIX') ManagedCodecs doesn't read. Either way its header counts more frames than its chunks.
        if (count != strh.Length)
            throw new UnsupportedVideoException(
                $"The video's header says {strh.Length} frames, its chunks are {count}: it drops frames (empty chunks), or it is an OpenDML AVI over 1 GB. Neither is supported yet.");

        if (header.Audio.FirstOrDefault() is { } audio)
            Audio = new AudioStream(audio.Codec, audio.SampleRate, audio.Channels);

        // FFmpeg's MJPEG decoder: a JPEG is full range (yuvj420p), its matrix BT.601 (bt470bg). The rate is the time
        // base's inverse (avi_read_header's avg_frame_rate), the frame count the header's dwLength.
        Video = new YuvVideo
        {
            Width = width,
            Height = height,
            FrameRate = new Fps(timeBase.Denominator, timeBase.Numerator),
            PixelFormat = "yuvj420p",
            ColorSpace = "bt470bg",
            FullRange = true,
            TimeBase = new FrameReader.Rational(checked((int)timeBase.Numerator), checked((int)timeBase.Denominator)),
            StartPts = Math.Max(firstPts, 0),
            FrameCount = strh.Length,
            Duration = DurationOf(header.DurationUs),
            Codec = "mjpeg",
            Container = "avi",
        };
    }

    /// <summary>The reads that read while the packets' data is skipped: the chunks' headers (<see cref="TimingOnlyStream"/>).</summary>
    const int ChunkHeader = 12;

    /// <summary>Opens an AVI file on disk.</summary>
    public static new AviInput Open(string path) => new(() => File.OpenRead(path));

    /// <summary>Opens an AVI file held in memory.</summary>
    public static new AviInput Open(byte[] data) => new(() => new MemoryStream(data, writable: false));

    /// <summary>Opens an AVI file read through <paramref name="open"/>, a new readable, seekable stream on every call.</summary>
    public static new AviInput Open(Func<Stream> open) => new(open);

    static (int Width, int Height, PixelFormat Format) FirstFrame(AviDemuxer demuxer, VideoStreamInfo info)
    {
        var decoder = new MjpegDecoder(info);
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex != info.StreamIndex)
                continue;
            foreach (var frame in decoder.Decode(packet))
            {
                using (frame)
                    return (frame.Width, frame.Height, frame.Format);
            }
        }

        throw new UnsupportedVideoException("The video stream decodes to no frames.");
    }

    /// <summary>
    /// The frames from video packet <paramref name="fromPacket"/> on: every MJPEG frame stands alone, so the demuxer
    /// walks the chunks before it with their data skipped and the decoder starts there.
    /// </summary>
    private protected override IEnumerable<VideoFrame> DecodedFrames(int fromPacket)
    {
        using var stream = new TimingOnlyStream(OpenStream()) { ReadsThrough = ChunkHeader };
        using var demuxer = new AviDemuxer(stream);
        var info = demuxer.Video ?? throw new UnsupportedVideoException("The AVI file has no video stream.");
        var decoder = new MjpegDecoder(info);
        stream.SkipReads = fromPacket > 0;
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex != info.StreamIndex)
                continue;
            if (packet.Sequence < fromPacket)
            {
                // The demuxer reads a packet's data as it hands out the packet: from the next one on, read them.
                if (packet.Sequence == fromPacket - 1)
                    stream.SkipReads = false;
                continue;
            }

            foreach (var frame in decoder.Decode(packet))
                yield return frame;
        }
    }
}
