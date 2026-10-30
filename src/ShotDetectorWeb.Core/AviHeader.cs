using System.Buffers.Binary;

namespace ShotDetectorWeb;

/// <summary>
/// What FFmpeg's AVI demuxer reads from an AVI file's headers ('hdrl': each stream's 'strh' and 'strf') that ManagedCodecs'
/// <see cref="ManagedCodecs.Containers.AviDemuxer"/> doesn't expose: the audio streams (their codec as ffprobe names it,
/// rate, channels, and the length FFmpeg takes as their duration) and the video stream's own rate and length. Only the
/// first RIFF part's headers: an OpenDML file's later parts ('AVIX') have none.
/// </summary>
sealed class AviHeader
{
    /// <summary>One stream's 'strh' (AVISTREAMHEADER) and, for audio, its 'strf' (WAVEFORMATEX).</summary>
    /// <param name="Type">fccType: 'vids', 'auds', ...</param>
    /// <param name="Scale">dwScale: the stream's time base is <see cref="Scale"/>/<see cref="Rate"/>.</param>
    /// <param name="Rate">dwRate.</param>
    /// <param name="Length">dwLength, in the time base: FFmpeg's nb_frames.</param>
    /// <param name="SampleSize">dwSampleSize: 0 for a stream of variable-size chunks (video, VBR audio).</param>
    /// <param name="FormatTag">The audio's wFormatTag (an extensible format's subformat), else 0.</param>
    /// <param name="Channels">The audio's nChannels.</param>
    /// <param name="SampleRate">The audio's nSamplesPerSec.</param>
    /// <param name="BitsPerSample">The audio's wBitsPerSample.</param>
    internal sealed record Stream(string Type, uint Scale, uint Rate, uint Length, uint SampleSize, int FormatTag = 0,
        int Channels = 0, int SampleRate = 0, int BitsPerSample = 0)
    {
        /// <summary>
        /// The stream's duration in microseconds as FFmpeg's demuxer gives it (avi_read_header): dwLength in the time base
        /// for a stream without a sample size (video, VBR audio), none for one with it (PCM, ADPCM: FFmpeg leaves it
        /// unknown, so it doesn't count for the format's duration).
        /// </summary>
        public long? DurationUs => SampleSize == 0 && Scale > 0 && Rate > 0
            ? Mp4Header.Rescale((long)Length * Scale, 1_000_000, Rate)
            : null;

        /// <summary>The audio's codec as ffprobe names it (libavformat's WAV tags; PCM by its bits per sample).</summary>
        public string Codec => FormatTag switch
        {
            0x0001 => BitsPerSample switch
            {
                8 => "pcm_u8",
                24 => "pcm_s24le",
                32 => "pcm_s32le",
                64 => "pcm_s64le",
                _ => "pcm_s16le",
            },
            0x0002 => "adpcm_ms",
            0x0003 => BitsPerSample == 64 ? "pcm_f64le" : "pcm_f32le",
            0x0006 => "pcm_alaw",
            0x0007 => "pcm_mulaw",
            0x0011 => "adpcm_ima_wav",
            0x0050 => "mp2",
            0x0055 => "mp3",
            0x00FF or 0x1600 or 0x706D => "aac",
            0x2000 => "ac3",
            0x2001 => "dts",
            0x674F or 0x6750 or 0x6751 => "vorbis",
            0xF1AC => "flac",
            _ => $"0x{FormatTag:x4}",
        };
    }

    AviHeader(List<Stream> streams) => Streams = streams;

    /// <summary>The streams in the file's order.</summary>
    public IReadOnlyList<Stream> Streams { get; }

    /// <summary>The first video stream, as FFmpeg's (and ManagedCodecs') first.</summary>
    public Stream? Video => Streams.FirstOrDefault(s => s.Type == "vids");

    /// <summary>The audio streams.</summary>
    public IEnumerable<Stream> Audio => Streams.Where(s => s.Type == "auds");

    /// <summary>
    /// FFmpeg's format duration in microseconds (update_stream_timings): every stream starts at 0, so the longest
    /// stream's duration; null when none has one.
    /// </summary>
    public long? DurationUs => Streams.Max(s => s.DurationUs);

    /// <summary>Whether <paramref name="stream"/> starts as an AVI file does ('RIFF', a size, 'AVI '). Leaves it at 0.</summary>
    public static bool IsAvi(System.IO.Stream stream)
    {
        Span<byte> head = stackalloc byte[12];
        stream.Position = 0;
        int n = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        return n == 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..].SequenceEqual("AVI "u8);
    }

    /// <summary>
    /// Reads the 'hdrl' list of the AVI file in <paramref name="stream"/> (from its start; left anywhere). A file that isn't
    /// AVI, or whose headers are cut short, throws <see cref="InvalidDataException"/>, as ManagedCodecs' demuxer would.
    /// </summary>
    public static AviHeader Read(System.IO.Stream stream)
    {
        if (!IsAvi(stream))
            throw new InvalidDataException("Not an AVI file.");
        stream.Position = 12;
        Span<byte> chunk = stackalloc byte[12];
        while (true)
        {
            if (stream.ReadAtLeast(chunk, 12, throwOnEndOfStream: false) < 12)
                throw new InvalidDataException("Malformed AVI file: no 'hdrl' list.");
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("LIST"u8) && chunk[8..].SequenceEqual("hdrl"u8))
            {
                // The headers are small (a few hundred bytes a stream, with padding 'JUNK'): more than 16 MB is broken.
                if (size < 4 || size > 16 << 20)
                    throw new InvalidDataException("Malformed AVI file: its 'hdrl' list is too large.");
                var list = new byte[size - 4];
                stream.ReadExactly(list);
                return new AviHeader(StreamsOf(list));
            }

            stream.Position += size + (size & 1) - 4;
        }
    }

    static List<Stream> StreamsOf(ReadOnlySpan<byte> hdrl)
    {
        var streams = new List<Stream>();
        foreach (var (id, strl) in Chunks(hdrl))
        {
            if (id != "LIST" || strl.Length < 4 || !strl[..4].SequenceEqual("strl"u8))
                continue;
            Stream? stream = null;
            foreach (var (part, data) in Chunks(strl[4..]))
            {
                if (part == "strh" && data.Length >= 36)
                {
                    stream = new Stream(
                        System.Text.Encoding.ASCII.GetString(data[..4]),
                        Scale: BinaryPrimitives.ReadUInt32LittleEndian(data[20..]),
                        Rate: BinaryPrimitives.ReadUInt32LittleEndian(data[24..]),
                        Length: BinaryPrimitives.ReadUInt32LittleEndian(data[32..]),
                        SampleSize: data.Length >= 48 ? BinaryPrimitives.ReadUInt32LittleEndian(data[44..]) : 0);
                }
                else if (part == "strf" && stream is { Type: "auds" } && data.Length >= 16)
                {
                    int tag = BinaryPrimitives.ReadUInt16LittleEndian(data);
                    int bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
                    // WAVE_FORMAT_EXTENSIBLE: the format is the subformat GUID's first two bytes.
                    if (tag == 0xFFFE && data.Length >= 26)
                        tag = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
                    stream = stream with
                    {
                        FormatTag = tag,
                        Channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
                        SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
                        BitsPerSample = bits,
                    };
                }
            }

            if (stream is not null)
                streams.Add(stream);
        }

        return streams;
    }

    /// <summary>The RIFF chunks in <paramref name="data"/>: each id and its data (a list's form type first).</summary>
    static ChunkEnumerable Chunks(ReadOnlySpan<byte> data) => new(data);

    readonly ref struct Chunk(string id, ReadOnlySpan<byte> data)
    {
        public string Id { get; } = id;

        public ReadOnlySpan<byte> Data { get; } = data;

        public void Deconstruct(out string id, out ReadOnlySpan<byte> data)
        {
            id = Id;
            data = Data;
        }
    }

    ref struct ChunkEnumerable(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> _rest = data;

        public Chunk Current { get; private set; }

        public readonly ChunkEnumerable GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_rest.Length < 8)
                return false;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(_rest[4..]);
            if (size > _rest.Length - 8)
                throw new InvalidDataException("Malformed AVI file: a header chunk runs past its list.");
            Current = new Chunk(System.Text.Encoding.ASCII.GetString(_rest[..4]), _rest.Slice(8, (int)size));
            int next = 8 + (int)size + (int)(size & 1);
            _rest = next >= _rest.Length ? [] : _rest[next..];
            return true;
        }
    }
}
