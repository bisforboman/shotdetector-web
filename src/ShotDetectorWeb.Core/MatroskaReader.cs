using System.Buffers.Binary;
using System.Text;

namespace ShotDetectorWeb;

/// <summary>
/// What FFmpeg's matroska demuxer reads of a WebM or Matroska file that <see cref="WebmInput"/> needs, read the way it
/// reads it (decision 2026-11-02-webm-browser-decoder.md; ManagedCodecs has no Matroska demuxer): the EBML header's
/// DocType, the Segment's Info (TimestampScale, Duration), the first video track and the first audio track (codec,
/// CodecPrivate, DefaultDuration, size, Colour, Projection, sampling), and the video track's blocks, each with its
/// timestamp, size, keyframe flag and where its data is. What it doesn't take (laced or encoded video, a track timestamp
/// scale) throws <see cref="UnsupportedVideoException"/>; a file cut short or not EBML, <see cref="InvalidDataException"/>.
/// </summary>
public sealed class MatroskaReader
{
    // Element ids, their marker bits kept, as the Matroska specification writes them.
    const uint Ebml = 0x1A45DFA3, DocTypeId = 0x4282, Segment = 0x18538067, Info = 0x1549A966, TimestampScaleId = 0x2AD7B1,
        DurationId = 0x4489, Tracks = 0x1654AE6B, TrackEntry = 0xAE, TrackNumber = 0xD7, TrackType = 0x83, CodecId = 0x86,
        CodecPrivateId = 0x63A2, DefaultDurationId = 0x23E383, TrackTimestampScale = 0x23314F, ContentEncodings = 0x6D80,
        VideoId = 0xE0, PixelWidth = 0xB0, PixelHeight = 0xBA, FlagInterlaced = 0x9A, Colour = 0x55B0,
        MatrixCoefficients = 0x55B1, Range = 0x55B9, ChromaSitingHorz = 0x55B7, ChromaSitingVert = 0x55B8,
        Projection = 0x7670, ProjectionPoseYaw = 0x7673, ProjectionPosePitch = 0x7674, ProjectionPoseRoll = 0x7675,
        AudioId = 0xE1, SamplingFrequency = 0xB5, Channels = 0x9F, Cluster = 0x1F43B675, ClusterTimestamp = 0xE7,
        SimpleBlock = 0xA3, BlockGroup = 0xA0, Block = 0xA1, ReferenceBlock = 0xFB, BlockDuration = 0x9B,
        FrameRateId = 0x2383E3;

    // The Segment's children: an element of unknown size (a live stream's Cluster) ends where one of these starts.
    static readonly HashSet<uint> SegmentChildren =
        [0x114D9B74, Info, Tracks, Cluster, 0x1C53BB6B, 0x1043A770, 0x1254C367, 0x1941A469];

    MatroskaReader() { }

    /// <summary>The EBML header's DocType: <c>webm</c> or <c>matroska</c>.</summary>
    public string DocType { get; private set; } = "";

    /// <summary>The Segment's TimestampScale, nanoseconds a tick (1,000,000 unless the file says).</summary>
    public long TimestampScale { get; private set; } = 1_000_000;

    /// <summary>The Segment's Duration in ticks, a float as written; null without one (a live stream's).</summary>
    public double? Duration { get; private set; }

    /// <summary>The first video track.</summary>
    public VideoTrack Video { get; private set; } = null!;

    /// <summary>The first audio track, or null.</summary>
    public AudioTrack? Audio { get; private set; }

    /// <summary>Where the Segment's data starts and ends in the file (an unknown size: the file's end).</summary>
    long _segmentStart, _segmentEnd;

    /// <summary>A Matroska video track as its TrackEntry says it.</summary>
    public sealed record VideoTrack(
        long Number, string CodecId, byte[] CodecPrivate, long? DefaultDuration, int Width, int Height, int Interlaced,
        int? MatrixCoefficients, int? Range, int? ChromaSitingHorz, int? ChromaSitingVert, bool HasProjection);

    /// <summary>A Matroska audio track as its TrackEntry says it.</summary>
    public sealed record AudioTrack(long Number, string CodecId, double SamplingFrequency, int Channels);

    /// <summary>A video block: its timestamp in ticks (the Cluster's plus the block's own), size, flag and data's place.</summary>
    public readonly record struct VideoBlock(long Timestamp, int Size, bool Keyframe, long Offset);

    /// <summary>
    /// A block of any track as FFmpeg's probing reads it (<see cref="Packets"/>): whether it is the video track's, its
    /// timestamp, its size (its frames' data, the header left out) and whether its BlockGroup has a BlockDuration.
    /// </summary>
    public readonly record struct Packet(bool Video, long Timestamp, int Size, bool HasDuration);

    /// <summary>Reads the headers of the WebM or Matroska file in <paramref name="stream"/> (from its start).</summary>
    public static MatroskaReader Read(Stream stream)
    {
        var reader = new MatroskaReader();
        stream.Position = 0;
        var (id, size, start) = Element(stream);
        if (id != Ebml)
            throw new InvalidDataException("Not an EBML file.");
        foreach (var (childId, childSize, childStart) in Children(stream, start, start + size))
        {
            if (childId == DocTypeId)
                reader.DocType = Text(stream, childStart, childSize);
        }

        stream.Position = start + size;
        (id, size, start) = Element(stream);
        if (id != Segment)
            throw new InvalidDataException("The file has no Matroska Segment after its EBML header.");
        reader._segmentStart = start;
        reader._segmentEnd = size < 0 ? stream.Length : Math.Min(stream.Length, start + size);

        bool info = false, tracks = false;
        foreach (var (childId, childSize, childStart) in Children(stream, reader._segmentStart, reader._segmentEnd))
        {
            if (childId == Info)
            {
                reader.ReadInfo(stream, childStart, childStart + childSize);
                info = true;
            }
            else if (childId == Tracks)
            {
                reader.ReadTracks(stream, childStart, childStart + childSize);
                tracks = true;
            }
            else if (childId == Cluster)
            {
                break;
            }

            if (info && tracks)
                break;
        }

        if (!tracks || reader.Video is null)
            throw new UnsupportedVideoException("The Matroska file has no video track.");
        return reader;
    }

    void ReadInfo(Stream stream, long start, long end)
    {
        foreach (var (id, size, at) in Children(stream, start, end))
        {
            if (id == TimestampScaleId)
                TimestampScale = (long)Unsigned(stream, at, size);
            else if (id == DurationId)
                Duration = Float(stream, at, size);
        }
    }

    void ReadTracks(Stream stream, long start, long end)
    {
        foreach (var (id, size, at) in Children(stream, start, end))
        {
            if (id != TrackEntry)
                continue;
            long number = 0;
            int type = 0, width = 0, height = 0, interlaced = 0, channels = 1;
            string codec = "";
            byte[] codecPrivate = [];
            long? defaultDuration = null;
            int? matrix = null, range = null, sitingH = null, sitingV = null;
            bool projection = false, encoded = false;
            double? trackScale = null, frameRate = null;
            double samplingFrequency = 8000;
            foreach (var (childId, childSize, childAt) in Children(stream, at, at + size))
            {
                switch (childId)
                {
                    case TrackNumber: number = (long)Unsigned(stream, childAt, childSize); break;
                    case TrackType: type = (int)Unsigned(stream, childAt, childSize); break;
                    case CodecId: codec = Text(stream, childAt, childSize); break;
                    case CodecPrivateId: codecPrivate = Bytes(stream, childAt, childSize); break;
                    case DefaultDurationId: defaultDuration = (long)Unsigned(stream, childAt, childSize); break;
                    case TrackTimestampScale: trackScale = Float(stream, childAt, childSize); break;
                    case ContentEncodings: encoded = true; break;
                    case VideoId:
                        foreach (var (v, vSize, vAt) in Children(stream, childAt, childAt + childSize))
                        {
                            switch (v)
                            {
                                case PixelWidth: width = (int)Unsigned(stream, vAt, vSize); break;
                                case PixelHeight: height = (int)Unsigned(stream, vAt, vSize); break;
                                case FlagInterlaced: interlaced = (int)Unsigned(stream, vAt, vSize); break;
                                case FrameRateId: frameRate = Float(stream, vAt, vSize); break;
                                case Projection:
                                    foreach (var (p, pSize, pAt) in Children(stream, vAt, vAt + vSize))
                                    {
                                        if (p is ProjectionPoseYaw or ProjectionPosePitch or ProjectionPoseRoll && Float(stream, pAt, pSize) != 0)
                                            projection = true;
                                    }

                                    break;
                                case Colour:
                                    foreach (var (c, cSize, cAt) in Children(stream, vAt, vAt + vSize))
                                    {
                                        switch (c)
                                        {
                                            case MatrixCoefficients: matrix = (int)Unsigned(stream, cAt, cSize); break;
                                            case Range: range = (int)Unsigned(stream, cAt, cSize); break;
                                            case ChromaSitingHorz: sitingH = (int)Unsigned(stream, cAt, cSize); break;
                                            case ChromaSitingVert: sitingV = (int)Unsigned(stream, cAt, cSize); break;
                                        }
                                    }

                                    break;
                            }
                        }

                        break;
                    case AudioId:
                        foreach (var (a, aSize, aAt) in Children(stream, childAt, childAt + childSize))
                        {
                            if (a == SamplingFrequency)
                                samplingFrequency = Float(stream, aAt, aSize);
                            else if (a == Channels)
                                channels = (int)Unsigned(stream, aAt, aSize);
                        }

                        break;
                }
            }

            if (type == 1 && Video is null)
            {
                if (encoded)
                    throw new UnsupportedVideoException("The video track is compressed or encrypted (ContentEncodings): not supported.");
                if (trackScale is double scale && scale != 1)
                    throw new UnsupportedVideoException("The video track has a TrackTimestampScale: not supported.");
                // FFmpeg's matroskadec: the deprecated FrameRate stands for a DefaultDuration the track hasn't (1e9 / it,
                // a double made a whole number of nanoseconds).
                if (defaultDuration is null or 0 && frameRate is double fr && fr > 0 && 1_000_000_000 / fr <= long.MaxValue)
                    defaultDuration = (long)(1_000_000_000 / fr);
                Video = new VideoTrack(number, codec, codecPrivate, defaultDuration, width, height, interlaced, matrix, range,
                    sitingH, sitingV, projection);
            }
            else if (type == 2 && Audio is null)
            {
                Audio = new AudioTrack(number, codec, samplingFrequency, channels);
            }
        }
    }

    /// <summary>
    /// The video track's blocks in file order (WebM's: display order, no B-frames), each with where its data is; the
    /// other tracks' blocks skipped. Reads the block headers only; the data is read with <see cref="ReadData"/>.
    /// </summary>
    public IEnumerable<VideoBlock> Blocks(Stream stream)
    {
        foreach (var (block, _, _) in AllBlocks(stream))
        {
            if (block is { } video)
                yield return video;
        }
    }

    /// <summary>Every track's blocks in file order, as FFmpeg's probing reads them (its 5 MB counts them all).</summary>
    public IEnumerable<Packet> Packets(Stream stream)
    {
        foreach (var (block, otherSize, hasDuration) in AllBlocks(stream))
            yield return block is { } video ? new Packet(true, video.Timestamp, video.Size, hasDuration) : new Packet(false, 0, otherSize, hasDuration);
    }

    // The blocks in file order: the video track's as a VideoBlock, another track's by its size; and whether the block's
    // BlockGroup has a BlockDuration.
    IEnumerable<(VideoBlock? Video, int OtherSize, bool HasDuration)> AllBlocks(Stream stream)
    {
        long position = _segmentStart;
        while (position < _segmentEnd)
        {
            stream.Position = position;
            var (id, size, start) = Element(stream, atEnd: true);
            if (id == 0)
                yield break;
            long end = size < 0 ? ClusterEnd(stream, start) : start + size;
            if (id == Cluster)
            {
                long clusterTimestamp = 0;
                foreach (var (childId, childSize, childAt) in Children(stream, start, end))
                {
                    if (childId == ClusterTimestamp)
                    {
                        clusterTimestamp = (long)Unsigned(stream, childAt, childSize);
                    }
                    else if (childId == SimpleBlock)
                    {
                        var block = BlockOf(stream, childAt, childSize, clusterTimestamp, simple: true, keyframe: false, out int other);
                        yield return (block, other, false);
                    }
                    else if (childId == BlockGroup)
                    {
                        long blockAt = -1, blockSize = 0;
                        bool references = false, duration = false;
                        foreach (var (g, gSize, gAt) in Children(stream, childAt, childAt + childSize))
                        {
                            if (g == Block)
                                (blockAt, blockSize) = (gAt, gSize);
                            else if (g == ReferenceBlock)
                                references = true;
                            else if (g == BlockDuration && Unsigned(stream, gAt, gSize) > 0)
                                duration = true;
                        }

                        if (blockAt >= 0)
                        {
                            var block = BlockOf(stream, blockAt, blockSize, clusterTimestamp, simple: false, keyframe: !references, out int other);
                            yield return (block, other, duration);
                        }
                    }
                }
            }

            position = end;
        }
    }

    /// <summary>Reads block <paramref name="block"/>'s data.</summary>
    public static byte[] ReadData(Stream stream, VideoBlock block)
    {
        var data = new byte[block.Size];
        stream.Position = block.Offset;
        stream.ReadExactly(data);
        return data;
    }

    VideoBlock? BlockOf(Stream stream, long at, long size, long clusterTimestamp, bool simple, bool keyframe, out int otherSize)
    {
        stream.Position = at;
        long track = VarInt(stream, out int length, keepMarker: false);
        otherSize = (int)Math.Max(0, size - length - 3);
        if (track != Video.Number)
            return null;
        Span<byte> header = stackalloc byte[3];
        stream.ReadExactly(header);
        short relative = BinaryPrimitives.ReadInt16BigEndian(header);
        byte flags = header[2];
        if ((flags & 0x06) != 0)
            throw new UnsupportedVideoException("The video track's blocks are laced: not supported.");
        int headerLength = length + 3;
        return new VideoBlock(clusterTimestamp + relative, checked((int)(size - headerLength)),
            simple ? (flags & 0x80) != 0 : keyframe, at + headerLength);
    }

    // A Cluster of unknown size ends where the next Segment child starts (or the Segment does).
    long ClusterEnd(Stream stream, long start)
    {
        long position = start;
        while (position < _segmentEnd)
        {
            stream.Position = position;
            var (id, size, childStart) = Element(stream, atEnd: true);
            if (id == 0 || SegmentChildren.Contains(id) || size < 0)
                return position;
            position = childStart + size;
        }

        return _segmentEnd;
    }

    /// <summary>The children of the element whose data is <paramref name="start"/> to <paramref name="end"/>.</summary>
    static IEnumerable<(uint Id, long Size, long Start)> Children(Stream stream, long start, long end)
    {
        long position = start;
        while (position < end)
        {
            stream.Position = position;
            var (id, size, childStart) = Element(stream, atEnd: true);
            if (id == 0)
                yield break;
            if (size < 0)
                size = end - childStart;
            yield return (id, size, childStart);
            position = childStart + size;
        }
    }

    /// <summary>An element's id (marker kept), data size (-1 unknown) and where its data starts.</summary>
    static (uint Id, long Size, long Start) Element(Stream stream, bool atEnd = false)
    {
        if (atEnd && stream.Position >= stream.Length)
            return (0, 0, stream.Position);
        uint id = (uint)VarInt(stream, out _, keepMarker: true);
        long size = VarInt(stream, out int length, keepMarker: false);
        // All ones: an unknown size.
        if (size == (1L << (7 * length)) - 1)
            size = -1;
        return (id, size, stream.Position);
    }

    static long VarInt(Stream stream, out int length, bool keepMarker)
    {
        int first = stream.ReadByte();
        if (first < 0)
            throw new InvalidDataException("The Matroska file ends inside an element's header.");
        length = 1;
        while (length <= 8 && (first & (0x80 >> (length - 1))) == 0)
            length++;
        if (length > 8)
            throw new InvalidDataException("A Matroska element's header is malformed.");
        long value = keepMarker ? first : first & ((0x80 >> (length - 1)) - 1);
        for (int i = 1; i < length; i++)
        {
            int b = stream.ReadByte();
            if (b < 0)
                throw new InvalidDataException("The Matroska file ends inside an element's header.");
            value = (value << 8) | (uint)b;
        }

        return value;
    }

    static byte[] Bytes(Stream stream, long at, long size)
    {
        if (size > 16 << 20)
            throw new InvalidDataException("A Matroska header element is over 16 MB.");
        var data = new byte[size];
        stream.Position = at;
        stream.ReadExactly(data);
        return data;
    }

    static ulong Unsigned(Stream stream, long at, long size)
    {
        ulong value = 0;
        foreach (byte b in Bytes(stream, at, Math.Min(size, 8)))
            value = (value << 8) | b;
        return value;
    }

    static double Float(Stream stream, long at, long size) => size switch
    {
        4 => BinaryPrimitives.ReadSingleBigEndian(Bytes(stream, at, 4)),
        8 => BinaryPrimitives.ReadDoubleBigEndian(Bytes(stream, at, 8)),
        0 => 0,
        _ => throw new InvalidDataException("A Matroska float is neither 4 nor 8 bytes."),
    };

    static string Text(Stream stream, long at, long size) => Encoding.UTF8.GetString(Bytes(stream, at, size)).TrimEnd('\0');
}
