namespace ShotDetectorWeb;

/// <summary>
/// The words of a refusal for a file whose container or codec the core doesn't read (decision
/// 2026-11-02-browser-decoder.md, option 3): the codec or container named as a viewer knows it and as FFmpeg names it
/// (what <c>ffprobe</c> and the <c>shotdetect</c> CLI say), then what the page reads. What to do instead is the page's.
/// </summary>
public static class Refusal
{
    /// <summary>What the core reads, the end of every codec and container refusal.</summary>
    public const string Scope = "this page reads H.264 in MP4, MJPEG and H.264 in AVI, and VP9 and AV1 in WebM and MP4 so far.";

    /// <summary>
    /// An MP4 video track's sample entry as a viewer and FFmpeg name its codec: <c>HEVC (H.265, FFmpeg's hevc; 'hvc1')</c>;
    /// an entry the table doesn't know as itself (<c>'xxxx'</c>).
    /// </summary>
    public static string Mp4Codec(string sampleEntry) => sampleEntry switch
    {
        "hvc1" or "hev1" => Named("HEVC (H.265", "hevc", sampleEntry),
        "dvh1" or "dvhe" => Named("Dolby Vision HEVC (H.265", "hevc", sampleEntry),
        "vp09" => Named("VP9", "vp9", sampleEntry),
        "vp08" => Named("VP8", "vp8", sampleEntry),
        "av01" => Named("AV1", "av1", sampleEntry),
        "dav1" => Named("Dolby Vision AV1", "av1", sampleEntry),
        "mp4v" => Named("MPEG-4 Part 2", "mpeg4", sampleEntry),
        "s263" or "h263" => Named("H.263", "h263", sampleEntry),
        "apch" or "apcn" or "apcs" or "apco" or "ap4h" or "ap4x" => Named("ProRes", "prores", sampleEntry),
        "jpeg" or "mjpa" or "mjpb" => Named("Motion JPEG", "mjpeg", sampleEntry),
        "vvc1" or "vvi1" => Named("VVC (H.266", "vvc", sampleEntry),
        _ => $"'{sampleEntry}'",
    };

    /// <summary>An AVI video stream's codec (ManagedCodecs' id: its FourCC) as a viewer and FFmpeg name it.</summary>
    public static string AviCodec(string fourCc) => fourCc.ToUpperInvariant() switch
    {
        "H264" or "X264" or "AVC1" => Named("H.264", "h264", fourCc),
        "HEVC" or "H265" or "HVC1" => Named("HEVC (H.265", "hevc", fourCc),
        "MP4V" or "XVID" or "DIVX" or "DX50" or "FMP4" => Named("MPEG-4 Part 2", "mpeg4", fourCc),
        "VP90" => Named("VP9", "vp9", fourCc),
        "AV01" => Named("AV1", "av1", fourCc),
        _ => $"'{fourCc}'",
    };

    // "HEVC (H.265" closes its own parenthesis with FFmpeg's name: "HEVC (H.265, FFmpeg's hevc; 'hvc1')".
    static string Named(string name, string ffmpeg, string tag) =>
        name.Contains('(') ? $"{name}, FFmpeg's {ffmpeg}; '{tag}')" : $"{name} (FFmpeg's {ffmpeg}; '{tag}')";

    /// <summary>
    /// The container of a file that is neither MP4 nor AVI, by its first bytes, as a viewer and FFmpeg name it (WebM and
    /// Matroska by the EBML header's DocType); null when they don't say (the MP4 reader then says it isn't one).
    /// </summary>
    public static string? OtherContainer(Stream stream)
    {
        Span<byte> head = stackalloc byte[64];
        stream.Position = 0;
        int n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        head = head[..n];
        if (head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return DocType(head) switch
            {
                "webm" => "a WebM file (FFmpeg's matroska,webm), most often VP9 or AV1",
                _ => "a Matroska file (FFmpeg's matroska,webm; .mkv)",
            };
        if (n > 0 && head[0] == 0x47 && Ts(stream))
            return "an MPEG transport stream (FFmpeg's mpegts; .ts, .m2ts)";
        if (head.StartsWith("OggS"u8))
            return "an Ogg file (FFmpeg's ogg)";
        if (head.StartsWith("FLV"u8))
            return "a Flash video (FFmpeg's flv)";
        if (head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0xBA]))
            return "an MPEG program stream (FFmpeg's mpeg; .mpg, .vob)";
        if (head.StartsWith((ReadOnlySpan<byte>)[0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]))
            return "a Windows Media file (FFmpeg's asf; .wmv)";
        return null;
    }

    /// <summary>Whether the file starts with an EBML header: WebM or Matroska.</summary>
    public static bool IsMatroska(Stream stream)
    {
        Span<byte> head = stackalloc byte[4];
        stream.Position = 0;
        int n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        return n == 4 && head.SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]);
    }

    // The EBML header's DocType (element 0x4282), an ASCII string, if it is in the first bytes.
    static string? DocType(ReadOnlySpan<byte> head)
    {
        int at = head.IndexOf((ReadOnlySpan<byte>)[0x42, 0x82]);
        if (at < 0 || at + 3 > head.Length || (head[at + 2] & 0x80) == 0)
            return null;
        int length = head[at + 2] & 0x7F;
        return at + 3 + length <= head.Length ? System.Text.Encoding.ASCII.GetString(head.Slice(at + 3, length)) : null;
    }

    // A transport stream: a sync byte every 188 bytes, three packets running.
    static bool Ts(Stream stream)
    {
        Span<byte> packets = stackalloc byte[188 * 3 + 1];
        int n = stream.ReadAtLeast(packets, packets.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        return n == packets.Length && packets[0] == 0x47 && packets[188] == 0x47 && packets[376] == 0x47 && packets[564] == 0x47;
    }
}
