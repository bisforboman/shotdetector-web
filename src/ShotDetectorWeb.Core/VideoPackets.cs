namespace ShotDetectorWeb;

/// <summary>What a video packet is, as FFmpeg's packet flags say (ffprobe's K and D).</summary>
[Flags]
public enum PacketFlags : byte
{
    None = 0,

    /// <summary>A keyframe (AV_PKT_FLAG_KEY, ffprobe's K): the encoder's IDR pictures.</summary>
    Keyframe = 1,

    /// <summary>
    /// Decoded but not shown (AV_PKT_FLAG_DISCARD, ffprobe's D): outside the edit list, kept because the edit's frames
    /// depend on it.
    /// </summary>
    Discard = 2,
}

/// <summary>
/// The video track's packets in decode order, as FFmpeg's demuxer gives them (<c>ffprobe -show_packets</c>, edit list
/// applied, discarded packets included): each one's pts, size and flags. Kept by column, 13 bytes a packet (2.2 MB for a
/// two-hour film at 24 fps), for the bitrate over time and the keyframes.
/// </summary>
public sealed class VideoPackets
{
    readonly long[] _pts;
    readonly int[] _sizes;
    readonly PacketFlags[] _flags;
    readonly long[] _offsets;

    VideoPackets(FrameReader.Rational timeBase, long[] pts, int[] sizes, PacketFlags[] flags, long[] offsets)
    {
        TimeBase = timeBase;
        _pts = pts;
        _sizes = sizes;
        _flags = flags;
        _offsets = offsets;
    }

    /// <summary>The packets' time base (the track's 1 / timescale, <see cref="ShotDetector.YuvVideo.TimeBase"/>).</summary>
    public FrameReader.Rational TimeBase { get; }

    /// <summary>How many packets the track has.</summary>
    public int Count => _pts.Length;

    /// <summary>Each packet's presentation timestamp, in <see cref="TimeBase"/>.</summary>
    public ReadOnlySpan<long> Pts => _pts;

    /// <summary>Each packet's size in bytes.</summary>
    public ReadOnlySpan<int> Sizes => _sizes;

    /// <summary>Each packet's flags.</summary>
    public ReadOnlySpan<PacketFlags> Flags => _flags;

    /// <summary>
    /// Where each packet's data starts in the file, in bytes (its <see cref="Sizes"/> long), where a packet is a picture
    /// a browser shows on its own: an AVI's MJPEG chunks, each a JPEG (backlog "The picture of an AVI on the page"; the
    /// page cuts them from the picked file). Empty for an MP4, whose packets are H.264 that only a decoder shows.
    /// </summary>
    public ReadOnlySpan<long> Offsets => _offsets;

    /// <summary>Packet <paramref name="index"/>'s pts in seconds, as FFmpeg computes pts_time (pts × av_q2d(time base)).</summary>
    public double Seconds(int index) => _pts[index] * (TimeBase.Num / (double)TimeBase.Den);

    /// <summary>Collects the packets during a walk over the file, with room for the track's sample count to start with.</summary>
    internal sealed class Builder(FrameReader.Rational timeBase, int capacity)
    {
        long[] _pts = new long[capacity];
        int[] _sizes = new int[capacity];
        PacketFlags[] _flags = new PacketFlags[capacity];
        long[]? _offsets;
        int _count;

        public void Add(in ManagedCodecs.Packet packet) =>
            Add(packet.Pts, packet.Data.Length, packet.IsKeyframe, packet.Discard);

        public void Add(in ManagedCodecs.PacketInfo packet) =>
            Add(packet.Pts, packet.Size, packet.IsKeyframe, packet.Discard);

        /// <summary>A packet whose data starts at <paramref name="offset"/> in the file: every packet, or none, has one.</summary>
        public void Add(in ManagedCodecs.Packet packet, long offset)
        {
            _offsets ??= new long[_pts.Length];
            if (_count == _offsets.Length)
                Array.Resize(ref _offsets, Math.Max(4, _count * 2));
            _offsets[_count] = offset;
            Add(packet);
        }

        void Add(long pts, int size, bool keyframe, bool discard)
        {
            // An edit list can repeat samples (several edits) beyond the sample count.
            if (_count == _pts.Length)
            {
                int capacity = Math.Max(4, _count * 2);
                Array.Resize(ref _pts, capacity);
                Array.Resize(ref _sizes, capacity);
                Array.Resize(ref _flags, capacity);
            }

            _pts[_count] = pts;
            _sizes[_count] = size;
            _flags[_count] = (keyframe ? PacketFlags.Keyframe : 0) | (discard ? PacketFlags.Discard : 0);
            _count++;
        }

        public VideoPackets Build()
        {
            // An edit list that ends early leaves fewer packets than samples.
            if (_count != _pts.Length)
            {
                Array.Resize(ref _pts, _count);
                Array.Resize(ref _sizes, _count);
                Array.Resize(ref _flags, _count);
            }

            if (_offsets is not null && _offsets.Length != _count)
                Array.Resize(ref _offsets, _count);
            return new VideoPackets(timeBase, _pts, _sizes, _flags, _offsets ?? []);
        }
    }
}
