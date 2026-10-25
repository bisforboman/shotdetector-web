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

    VideoPackets(FrameReader.Rational timeBase, long[] pts, int[] sizes, PacketFlags[] flags)
    {
        TimeBase = timeBase;
        _pts = pts;
        _sizes = sizes;
        _flags = flags;
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

    /// <summary>Packet <paramref name="index"/>'s pts in seconds, as FFmpeg computes pts_time (pts × av_q2d(time base)).</summary>
    public double Seconds(int index) => _pts[index] * (TimeBase.Num / (double)TimeBase.Den);

    /// <summary>Collects the packets during a walk over the file, with room for the track's sample count to start with.</summary>
    internal sealed class Builder(FrameReader.Rational timeBase, int capacity)
    {
        long[] _pts = new long[capacity];
        int[] _sizes = new int[capacity];
        PacketFlags[] _flags = new PacketFlags[capacity];
        int _count;

        public void Add(in ManagedCodecs.Packet packet)
        {
            // An edit list can repeat samples (several edits) beyond the sample count.
            if (_count == _pts.Length)
            {
                int size = Math.Max(4, _count * 2);
                Array.Resize(ref _pts, size);
                Array.Resize(ref _sizes, size);
                Array.Resize(ref _flags, size);
            }

            _pts[_count] = packet.Pts;
            _sizes[_count] = packet.Data.Length;
            _flags[_count] = (packet.IsKeyframe ? PacketFlags.Keyframe : 0) | (packet.Discard ? PacketFlags.Discard : 0);
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

            return new VideoPackets(timeBase, _pts, _sizes, _flags);
        }
    }
}
