using System.Globalization;
using System.Runtime.CompilerServices;
using ManagedCodecs;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// A video file decoded with ManagedCodecs (no FFmpeg), described as FFmpeg describes it, so that
/// <see cref="ShotDetection.Detect(YuvVideo, IEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/> gives the
/// file's result, byte for byte, as <see cref="ShotDetection.Detect(string, DetectionOptions?, CancellationToken)"/>
/// with FFmpeg and the same converter does. One per container: <see cref="Mp4Input"/> (H.264 in MP4) and
/// <see cref="AviInput"/> (MJPEG in AVI) each describe the file and decode its frames; detection, pausing, the ranges
/// and the packets are the same for both (decision 2026-10-30-mjpeg-avi.md).
/// </summary>
/// <remarks>
/// What each container takes is on its class; anything else throws <see cref="UnsupportedVideoException"/> when opened.
/// Damaged files throw ManagedCodecs' <see cref="InvalidDataException"/>.
/// </remarks>
public abstract partial class VideoInput
{
    private protected VideoInput(Func<Stream> open) => OpenStream = open;

    /// <summary>A new readable, seekable stream over the file.</summary>
    private protected Func<Stream> OpenStream { get; }

    /// <summary>The video as FFmpeg's demuxer and decoder report it (average frame rate, time base, start pts, colour tags, frame count).</summary>
    public YuvVideo Video { get; private protected set; } = null!;

    /// <summary>The first audio track as FFmpeg's demuxer describes it (ffprobe's codec_name, sample_rate, channels); null without one.</summary>
    public AudioStream? Audio { get; private protected set; }

    /// <summary>The video's packets as FFmpeg's demuxer gives them (pts, size, keyframe), from the walk opening made.</summary>
    public VideoPackets Packets { get; private protected set; } = null!;

    /// <summary>
    /// How many pictures the decoder decodes at once, each on a thread of its own (ManagedCodecs'
    /// <see cref="ManagedCodecs.H264.H264Decoder.Threads"/>, frame threading): the same frames, in the same order, decoded
    /// faster on more cores. 1, the default, decodes on the thread that reads the frames, which is the only choice where
    /// there are no threads (WebAssembly without them). It applies to the frames read from here on (<see cref="Frames()"/>,
    /// <see cref="FramesAsync"/>, <see cref="DetectAsync"/>, <see cref="ScoreRangeAsync"/>); opening decodes the first
    /// frame on one. H.264's only: MJPEG decodes on the calling thread whatever it says.
    /// </summary>
    public int DecoderThreads
    {
        get => _decoderThreads;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _decoderThreads = value;
        }
    }

    int _decoderThreads = 1;

    /// <summary>Opens a video file on disk: an AVI or an MP4, by its first bytes.</summary>
    public static VideoInput Open(string path) => Open(() => File.OpenRead(path));

    /// <summary>Opens a video file held in memory (in the browser: the file the user picked).</summary>
    public static VideoInput Open(byte[] data) => Open(() => new MemoryStream(data, writable: false));

    /// <summary>
    /// Opens a video file read through <paramref name="open"/>, which must give a new readable, seekable stream over the
    /// file on every call (in the browser: one reading slices of the picked file, so it never has to fit in memory): an
    /// AVI (a RIFF file of form 'AVI ') as <see cref="AviInput"/>, anything else as <see cref="Mp4Input"/>, which says
    /// what it isn't.
    /// </summary>
    public static VideoInput Open(Func<Stream> open)
    {
        bool avi;
        using (var stream = open())
            avi = AviHeader.IsAvi(stream);
        return avi ? AviInput.Open(open) : Mp4Input.Open(open);
    }

    /// <summary>
    /// Detects shots: <see cref="ShotDetection.Detect(YuvVideo, IEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/>
    /// on <see cref="Frames"/>, with ShotDetector.FastYuv's converter unless the options name another.
    /// </summary>
    /// <param name="options">ShotDetector's options.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <param name="picture">Measures each frame as it is read (<see cref="PictureMeter"/>), if given.</param>
    public DetectionResult Detect(DetectionOptions? options = null, CancellationToken cancellationToken = default,
        PictureMeter? picture = null)
    {
        options = WithConverter(options);
        return ShotDetection.Detect(Video, Measured(Frames(0), picture), options, cancellationToken);
    }

    /// <summary>
    /// Detects shots as <see cref="Detect"/> does, through
    /// <see cref="ShotDetection.DetectAsync(YuvVideo, IAsyncEnumerable{YuvFrame}, DetectionOptions?, CancellationToken)"/>
    /// on <see cref="FramesAsync"/>: the same result, and between frames the caller's own awaits (in the browser, the
    /// worker's event loop), where it can end the frames early (<paramref name="between"/>) or cancel. Frames that end
    /// early are a file that ends there: the result is the one for the frames read, stats included (ShotDetector 3.16.1
    /// neither warns nor refuses when fewer frames come than <see cref="YuvVideo.FrameCount"/> says).
    /// </summary>
    public Task<DetectionResult> DetectAsync(int every, Func<int, ValueTask<bool>> between,
        DetectionOptions? options = null, CancellationToken cancellationToken = default, PictureMeter? picture = null)
    {
        options = WithConverter(options);
        return ShotDetection.DetectAsync(Video, FramesAsync(every, between, picture: picture), options, cancellationToken);
    }

    /// <summary>
    /// <see cref="Frames"/> as an asynchronous source: after every <paramref name="every"/> frames, before the next one
    /// is decoded, it awaits <paramref name="between"/> with the number of frames given so far; when that returns false,
    /// the frames end there, as if the file did. Each frame given is measured by <paramref name="picture"/>, if any.
    /// </summary>
    public IAsyncEnumerable<YuvFrame> FramesAsync(int every, Func<int, ValueTask<bool>> between,
        CancellationToken cancellationToken = default, PictureMeter? picture = null) =>
        Paused(Measured(Frames(0), picture), int.MaxValue, every, between, cancellationToken);

    /// <summary>
    /// <paramref name="frames"/>' frames, each measured by <paramref name="meter"/> with its picture type as it is given
    /// (none without one).
    /// </summary>
    static IEnumerable<YuvFrame> Measured(IEnumerable<(YuvFrame Frame, PictureType Type)> frames, PictureMeter? meter)
    {
        foreach (var (frame, type) in frames)
        {
            meter?.Add(frame, type);
            yield return frame;
        }
    }

    /// <summary>
    /// <paramref name="source"/>'s first <paramref name="limit"/> frames as an asynchronous source, awaiting
    /// <paramref name="between"/> every <paramref name="every"/> frames (<see cref="FramesAsync"/>).
    /// </summary>
    static async IAsyncEnumerable<YuvFrame> Paused(IEnumerable<YuvFrame> source, int limit, int every,
        Func<int, ValueTask<bool>> between, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(every, 1);
        ArgumentNullException.ThrowIfNull(between);
        using var frames = source.GetEnumerator();
        for (int given = 0; given < limit; given++)
        {
            if (given > 0 && given % every == 0 && !await between(given))
                yield break;
            cancellationToken.ThrowIfCancellationRequested();
            if (!frames.MoveNext())
                yield break;
            yield return frames.Current;
        }
    }

    /// <summary>
    /// The decoded frames in display order, each valid until the enumeration moves on. Every enumeration decodes the file
    /// again from its start.
    /// </summary>
    public IEnumerable<YuvFrame> Frames() => Measured(Frames(0), null);

    /// <summary>
    /// <see cref="Frames()"/> decoded from video packet <paramref name="fromPacket"/> (decode order), a keyframe, each with
    /// its picture type as the decoder says it (FFmpeg's <c>pict_type</c>).
    /// </summary>
    IEnumerable<(YuvFrame Frame, PictureType Type)> Frames(int fromPacket)
    {
        var format = DecodedFormat(Video.PixelFormat);
        foreach (var frame in DecodedFrames(fromPacket))
        {
            using (frame)
            {
                // A stream whose format changes midway (an SPS of another profile, a JPEG of other sampling).
                if (frame.Format != format)
                    throw new UnsupportedVideoException($"A frame came out as {frame.Format}, not {format} as the first: a format that changes midway is not supported.");
                if ((frame.Width, frame.Height) != (Video.Width, Video.Height))
                    throw new UnsupportedVideoException($"The frame size changes ({Video.Width}x{Video.Height} to {frame.Width}x{frame.Height}): not supported.");
                if (frame.IsCodedAsFields)
                    throw new UnsupportedVideoException(FieldPictures);
                var p = frame.Planes;
                var o = frame.Offsets;
                var s = frame.Strides;
                // FFmpeg's interlaced_frame and top_field_first, which set yadif's parity when the run deinterlaces. A
                // 10-bit frame's planes are 16-bit little-endian words, its strides in bytes: YuvFrame's layout as is.
                var yuv = new YuvFrame(p[0].AsMemory(o[0]), s[0], p[1].AsMemory(o[1]), s[1], p[2].AsMemory(o[2]), s[2])
                {
                    Pts = frame.Pts,
                    Interlaced = frame.IsInterlaced,
                    TopFieldFirst = frame.IsTopFieldFirst,
                };
                yield return (yuv, frame.PictureType);
            }
        }
    }

    /// <summary>ManagedCodecs' layout of the frames of FFmpeg's pixel format <paramref name="pixelFormat"/> (the video's).</summary>
    private protected static PixelFormat DecodedFormat(string pixelFormat) => pixelFormat switch
    {
        "yuv420p" or "yuvj420p" => PixelFormat.Yuv420P,
        "yuv422p" or "yuvj422p" => PixelFormat.Yuv422P,
        "yuv420p10le" => PixelFormat.Yuv420P10,
        _ => throw new UnsupportedVideoException($"The pixel format {pixelFormat} is not supported."),
    };

    /// <summary>
    /// Why a frame coded as two field pictures (PAFF) is refused: no encoder at hand makes a sample to check it on
    /// (decision 2026-10-30-interlaced.md), and ManagedCodecs says which frames they are (<c>VideoFrame.IsCodedAsFields</c>).
    /// </summary>
    private protected const string FieldPictures =
        "The video is interlaced coded as field pictures (PAFF): not supported yet (only MBAFF frames, which have a sample to check them on).";

    /// <summary>
    /// The frames decoded from video packet <paramref name="fromPacket"/> on (decode order; a keyframe, for a decoder that
    /// starts clean there), in display order. The packets before it are neither decoded nor read.
    /// </summary>
    private protected abstract IEnumerable<VideoFrame> DecodedFrames(int fromPacket);

    /// <summary>
    /// ffprobe's printed seconds as ShotDetector's file path reads them (3.15.0's VideoReader.Seconds): to the nearest
    /// tick, not TimeSpan.FromSeconds's tick below (4.56 would be 4.5599999).
    /// </summary>
    private protected static TimeSpan Seconds(string text) =>
        TimeSpan.FromTicks((long)Math.Round(double.Parse(text, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond));

    /// <summary>FFmpeg's format duration in microseconds as ffprobe prints it (six decimals), read as the file path reads it.</summary>
    private protected static TimeSpan? DurationOf(long? microseconds) => microseconds is long us
        ? Seconds((us * (1 / 1_000_000.0)).ToString("F6", CultureInfo.InvariantCulture))
        : null;
}

/// <summary>
/// Reads through to the stream it wraps until <see cref="SkipReads"/> is set; from then on a read longer than
/// <see cref="ReadsThrough"/> bytes reads nothing and reports the bytes as read, leaving the buffer as it was, so a
/// demuxer hands out packets with their timing and without their data (left as allocated).
/// </summary>
sealed class TimingOnlyStream(Stream inner) : Stream
{
    public bool SkipReads { get; set; }

    /// <summary>
    /// The longest read that still reads while <see cref="SkipReads"/> is set: 0 for an MP4, whose demuxer knows every
    /// packet when it opens; 12 for an AVI, whose demuxer reads each chunk's header (8 bytes, a list's 4 more) as it walks
    /// to the packet, then the packet's data in one read of its size.
    /// </summary>
    public int ReadsThrough { get; init; }

    /// <summary>
    /// Where the last read longer than <see cref="ReadsThrough"/> started, skipped or not: for an AVI, the data of the
    /// packet its demuxer just handed out (<see cref="VideoPackets.Offsets"/>).
    /// </summary>
    public long LongReadAt { get; private set; } = -1;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length <= ReadsThrough)
            return inner.Read(buffer);
        LongReadAt = Position;
        if (!SkipReads)
            return inner.Read(buffer);
        int n = (int)Math.Clamp(Length - Position, 0, buffer.Length);
        Position += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// An audio track as ffprobe describes it: its codec's FFmpeg name (codec_name: "aac", "mp3", "opus", "pcm_s16le", ...),
/// samples per second and channels. Whether it is measured is <see cref="Mp4Audio"/>'s business: only AAC in MP4 is.
/// </summary>
public sealed record AudioStream(string Codec, int SampleRate, int Channels);

/// <summary>A video this library can't take (yet): the message says what and why.</summary>
public sealed class UnsupportedVideoException(string message) : NotSupportedException(message);
