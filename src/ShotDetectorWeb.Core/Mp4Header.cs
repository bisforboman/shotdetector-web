using System.Buffers.Binary;

namespace ShotDetectorWeb;

/// <summary>
/// What FFmpeg's MP4 demuxer (mov.c) reads from the headers that ManagedCodecs' <c>Mp4Demuxer</c> doesn't expose: the
/// video track's 'stts' totals (FFmpeg's nb_frames and avg_frame_rate), 'mdhd' timescale, 'tkhd' matrix (rotation), the 'colr' box and the 'avcC' record of its 'avc1' sample entry;
/// the first audio track's codec as FFmpeg names it.
/// </summary>
internal sealed class Mp4Header
{
    /// <summary>The video track's media timescale (mdhd): the stream's time base is 1 over it.</summary>
    public uint Timescale { get; private init; }

    /// <summary>The sum of the 'stts' sample counts: FFmpeg's nb_frames.</summary>
    public long SampleCount { get; private init; }

    /// <summary>The sum of the 'stts' durations, in <see cref="Timescale"/>.</summary>
    public long TotalDuration { get; private init; }

    /// <summary>The 'stts' entries (sample count, sample delta).</summary>
    public IReadOnlyList<(uint Count, uint Delta)> TimeToSample { get; private init; } = [];

    /// <summary>The sample entry's four-character code ("avc1", "avc3", "hvc1", ...).</summary>
    public string SampleEntry { get; private init; } = "";

    /// <summary>The 'avcC' payload; empty without one.</summary>
    public ReadOnlyMemory<byte> AvcConfig { get; private init; }

    /// <summary>The 'colr' box's matrix coefficients (nclx or nclc); null without one.</summary>
    public int? ColrMatrix { get; private init; }

    /// <summary>The 'colr' nclx box's full range flag; null without one (nclc has none).</summary>
    public bool? ColrFullRange { get; private init; }

    /// <summary>Whether the track header's matrix rotates or flips the picture.</summary>
    public bool Transformed { get; private init; }

    /// <summary>Whether the file is fragmented ('mvex' in 'moov').</summary>
    public bool Fragmented { get; private init; }

    /// <summary>
    /// The first audio track's codec as FFmpeg names it (codec_name: "aac", "mp3", "opus", ...), from its sample entry
    /// and, for 'mp4a', the 'esds' object type; the sample entry's four characters where this doesn't know FFmpeg's name.
    /// Null without an audio track.
    /// </summary>
    public string? AudioCodec { get; private init; }

    /// <summary>Reads the headers, or explains why the file isn't an MP4 with a video track.</summary>
    public static Mp4Header Read(Stream stream)
    {
        var moov = FindMoov(stream) ?? throw new UnsupportedVideoException("This isn't an MP4 file (no 'moov' box found).");
        bool fragmented = false;
        ReadOnlyMemory<byte>? video = null, audio = null;
        foreach (var (type, payload) in Boxes(moov))
        {
            switch (type)
            {
                case "mvex":
                    fragmented = true;
                    break;
                case "trak" when video is null && HandlerType(payload) == "vide":
                    video = payload;
                    break;
                case "trak" when audio is null && HandlerType(payload) == "soun":
                    audio = payload;
                    break;
            }
        }

        if (video is not { } trak)
            throw new UnsupportedVideoException("The MP4 file has no video track.");

        var mdia = Child(trak, "mdia") ?? throw Malformed("mdia");
        var mdhd = (Child(mdia, "mdhd") ?? throw Malformed("mdhd")).Span;
        uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd[(mdhd[0] == 1 ? 20 : 12)..]);
        var stbl = Child(Child(mdia, "minf") ?? throw Malformed("minf"), "stbl") ?? throw Malformed("stbl");

        var stts = new List<(uint, uint)>();
        long count = 0, total = 0;
        if (Child(stbl, "stts") is { } sttsBox)
        {
            var s = sttsBox.Span;
            uint entries = BinaryPrimitives.ReadUInt32BigEndian(s[4..]);
            for (int i = 0; i < entries && 8 + (i * 8) + 8 <= s.Length; i++)
            {
                uint n = BinaryPrimitives.ReadUInt32BigEndian(s[(8 + (i * 8))..]);
                uint delta = BinaryPrimitives.ReadUInt32BigEndian(s[(12 + (i * 8))..]);
                stts.Add((n, delta));
                count += n;
                total += (long)n * delta;
            }
        }

        string entry = "";
        ReadOnlyMemory<byte> avcC = default;
        int? colrMatrix = null;
        bool? colrFull = null;
        if (Child(stbl, "stsd") is { } stsd && stsd.Length >= 16)
        {
            // Full box, entry count, then the first sample entry: size, type, and a visual sample entry's 78 bytes
            // before its child boxes (ISO/IEC 14496-12, 12.1.3).
            var first = stsd[8..];
            uint size = BinaryPrimitives.ReadUInt32BigEndian(first.Span);
            entry = System.Text.Encoding.Latin1.GetString(first.Span.Slice(4, 4));
            if (size >= 8 + 78 && size <= first.Length)
            {
                foreach (var (type, payload) in Boxes(first[(8 + 78)..(int)size]))
                {
                    if (type == "avcC")
                    {
                        avcC = payload;
                    }
                    else if (type == "colr" && payload.Length >= 10 && colrMatrix is null)
                    {
                        // FFmpeg's mov_read_colr: nclx (ISO) and nclc (QuickTime) carry primaries, transfer and matrix;
                        // nclx a full range flag after them.
                        var c = payload.Span;
                        string kind = System.Text.Encoding.Latin1.GetString(c[..4]);
                        if (kind is "nclx" or "nclc")
                        {
                            colrMatrix = BinaryPrimitives.ReadUInt16BigEndian(c[8..]);
                            if (kind == "nclx" && c.Length >= 11)
                                colrFull = (c[10] & 0x80) != 0;
                        }
                    }
                }
            }
        }

        return new Mp4Header
        {
            Timescale = timescale,
            SampleCount = count,
            TotalDuration = total,
            TimeToSample = stts,
            SampleEntry = entry,
            AvcConfig = avcC,
            ColrMatrix = colrMatrix,
            ColrFullRange = colrFull,
            Transformed = Child(trak, "tkhd") is { } tkhd && IsTransformed(tkhd.Span),
            Fragmented = fragmented,
            AudioCodec = audio is { } a ? AudioCodecName(a) : null,
        };
    }

    /// <summary>
    /// FFmpeg's name for an audio track's codec (mov.c): its first sample entry's code through ff_codec_movaudio_tags,
    /// then for 'mp4a' the 'esds' DecoderConfigDescriptor's objectTypeIndication through ff_mp4_obj_type (AAC when it
    /// has none, or one FFmpeg doesn't map).
    /// </summary>
    static string AudioCodecName(ReadOnlyMemory<byte> trak)
    {
        var stbl = Child(Child(Child(trak, "mdia") ?? default, "minf") ?? default, "stbl");
        var stsd = stbl is { } b ? Child(b, "stsd") : null;
        if (stsd is not { Length: >= 16 } d)
            return "";
        var first = d[8..];
        uint size = BinaryPrimitives.ReadUInt32BigEndian(first.Span);
        string entry = System.Text.Encoding.Latin1.GetString(first.Span.Slice(4, 4));
        if (entry != "mp4a")
        {
            return entry switch
            {
                "Opus" => "opus",
                "fLaC" => "flac",
                "ac-3" => "ac3",
                "ec-3" => "eac3",
                ".mp3" => "mp3",
                "alac" => "alac",
                "samr" => "amr_nb",
                "sawb" => "amr_wb",
                _ => entry,
            };
        }

        // An audio sample entry's 28 bytes (ISO/IEC 14496-12, 12.2.3), 16 or 36 more for QuickTime's versions 1 and 2,
        // then its child boxes.
        var e = first.Span;
        if (size < 8 + 28 || size > first.Length)
            return "aac";
        int version = BinaryPrimitives.ReadUInt16BigEndian(e[16..]);
        int children = 8 + 28 + version switch { 1 => 16, 2 => 36, _ => 0 };
        if (children > size || Child(first[children..(int)size], "esds") is not { } esds)
            return "aac";
        return ObjectType(esds.Span) switch
        {
            0x69 or 0x6B => "mp3",
            0xA5 => "ac3",
            0xA6 => "eac3",
            0xA9 => "dts",
            0xAD => "opus",
            0xDD => "vorbis",
            _ => "aac",
        };
    }

    /// <summary>
    /// An 'esds' box's objectTypeIndication (ISO/IEC 14496-1, 7.2.6.5 and 7.2.6.6): after its version and flags, the
    /// ES_Descriptor (tag 3) and its optional fields, the DecoderConfigDescriptor (tag 4) starts with it; null if malformed.
    /// </summary>
    static int? ObjectType(ReadOnlySpan<byte> esds)
    {
        int pos = 4;
        if (Descriptor(esds, ref pos) != 3 || pos + 3 > esds.Length)
            return null;
        int flags = esds[pos + 2];
        pos += 3;
        if ((flags & 0x80) != 0)
            pos += 2; // dependsOn_ES_ID
        if ((flags & 0x40) != 0 && pos < esds.Length)
            pos += 1 + esds[pos]; // URL
        if ((flags & 0x20) != 0)
            pos += 2; // OCR_ES_Id
        return pos < esds.Length && Descriptor(esds, ref pos) == 4 && pos < esds.Length ? esds[pos] : null;
    }

    /// <summary>A descriptor's tag, moving <paramref name="pos"/> past its tag and length to its payload; -1 if cut short.</summary>
    static int Descriptor(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length)
            return -1;
        int tag = data[pos++];
        for (int i = 0; i < 4; i++)
        {
            if (pos >= data.Length)
                return -1;
            if ((data[pos++] & 0x80) == 0)
                break;
        }

        return tag;
    }

    /// <summary>a × b / c rounded to the nearest, halves away from zero (FFmpeg's av_rescale).</summary>
    internal static long Rescale(long a, long b, long c) =>
        a >= 0 ? (long)(((Int128)a * b + (c / 2)) / c) : -(long)(((Int128)(-a) * b + (c / 2)) / c);

    /// <summary>Whether a 'tkhd' matrix's a, b, c, d (16.16) differ from the identity: a rotation or flip FFmpeg would apply.</summary>
    static bool IsTransformed(ReadOnlySpan<byte> tkhd)
    {
        int matrix = tkhd[0] == 1 ? 52 : 40;
        if (tkhd.Length < matrix + 36)
            return false;
        int a = BinaryPrimitives.ReadInt32BigEndian(tkhd[matrix..]);
        int b = BinaryPrimitives.ReadInt32BigEndian(tkhd[(matrix + 4)..]);
        int c = BinaryPrimitives.ReadInt32BigEndian(tkhd[(matrix + 12)..]);
        int d = BinaryPrimitives.ReadInt32BigEndian(tkhd[(matrix + 16)..]);
        return (a, b, c, d) != (0x10000, 0, 0, 0x10000);
    }

    static string HandlerType(ReadOnlyMemory<byte> trak)
    {
        var hdlr = Child(Child(trak, "mdia") ?? default, "hdlr");
        return hdlr is { Length: >= 12 } h ? System.Text.Encoding.Latin1.GetString(h.Span.Slice(8, 4)) : "";
    }

    static UnsupportedVideoException Malformed(string box) => new($"The MP4 file's video track has no '{box}' box.");

    static ReadOnlyMemory<byte>? Child(ReadOnlyMemory<byte> parent, string type)
    {
        foreach (var (t, payload) in Boxes(parent))
        {
            if (t == type)
                return payload;
        }

        return null;
    }

    static IEnumerable<(string Type, ReadOnlyMemory<byte> Payload)> Boxes(ReadOnlyMemory<byte> data)
    {
        int pos = 0;
        while (pos + 8 <= data.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.Span[pos..]);
            string type = System.Text.Encoding.Latin1.GetString(data.Span.Slice(pos + 4, 4));
            int header = 8;
            if (size == 1 && pos + 16 <= data.Length)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.Span[(pos + 8)..]);
                header = 16;
            }
            else if (size == 0)
            {
                size = data.Length - pos;
            }

            if (size < header || pos + size > data.Length)
                yield break;
            yield return (type, data.Slice(pos + header, (int)size - header));
            pos += (int)size;
        }
    }

    /// <summary>The top-level 'moov' box's payload, or null when the stream has none (or isn't box-structured).</summary>
    static ReadOnlyMemory<byte>? FindMoov(Stream stream)
    {
        Span<byte> header = stackalloc byte[16];
        long pos = 0, length = stream.Length;
        while (pos + 8 <= length)
        {
            stream.Position = pos;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = System.Text.Encoding.Latin1.GetString(header.Slice(4, 4));
            int headerSize = 8;
            if (size == 1)
            {
                stream.ReadExactly(header.Slice(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = length - pos;
            }

            if (size < headerSize || pos + size > length || !type.All(ch => ch is >= ' ' and <= '~'))
                return null;
            if (type == "moov")
            {
                if (size > 256 << 20)
                    throw new UnsupportedVideoException("The MP4 file's 'moov' box is over 256 MB.");
                var moov = new byte[size - headerSize];
                stream.ReadExactly(moov);
                return moov;
            }

            pos += size;
        }

        return null;
    }
}
