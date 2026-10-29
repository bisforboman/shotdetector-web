using System.Runtime.InteropServices;
using FrameReader;
using ManagedCodecs;
using ManagedCodecs.Aac;
using ManagedCodecs.Containers;

namespace ShotDetectorWeb;

/// <summary>
/// An MP4 file's sound, measured in one pass over its audio packets with ManagedCodecs' AAC decoder (no FFmpeg): a
/// waveform, the reference tone, silences, peaks and loudness. Each decoded packet is folded into what is measured and
/// dropped, so memory stays small whatever the file's length. Not exact as the scene and stats CSVs are: NativeCheck
/// compares each measure with FFmpeg's within a tolerance (README, Checks).
/// </summary>
public static class Mp4Audio
{
    /// <summary>Measures the audio of an MP4 file on disk.</summary>
    public static AudioMeasures Measure(string path, AudioOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        return Measure(stream, options, cancellationToken);
    }

    /// <summary>
    /// Measures the audio of the MP4 file in <paramref name="stream"/> (readable and seekable; not disposed). A file
    /// without an audio track, a track that isn't AAC, or one ManagedCodecs' decoder refuses gives measures with
    /// <see cref="AudioMeasures.NotMeasured"/> saying so; a damaged file throws ManagedCodecs' <see cref="InvalidDataException"/>.
    /// </summary>
    public static AudioMeasures Measure(Stream stream, AudioOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new AudioOptions();
        var reader = new AudioDataStream(stream);
        using var demuxer = new Mp4Demuxer(reader, leaveOpen: true);
        if (demuxer.Audio is not { } info)
            return AudioMeasures.Without("The file has no audio track.");
        if (info.Codec != CodecIds.Aac)
            return AudioMeasures.Without($"The audio is '{info.Codec}', not AAC: only AAC is measured.");
        AacDecoder decoder;
        try
        {
            decoder = new AacDecoder(info);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException)
        {
            return AudioMeasures.Without($"ManagedCodecs' AAC decoder can't decode this audio: {e.Message}");
        }

        // From here on only the audio packets' data is read (the video's would be read just to be dropped).
        reader.SkipReads = true;
        Measurer? measurer = null;
        TimeSpan? start = null;
        long firstDuration = 0;
        foreach (var read in demuxer.ReadPackets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (read.StreamIndex != info.StreamIndex)
                continue;
            var packet = reader.Fill(read);
            if (firstDuration == 0)
                firstDuration = packet.Duration;

            // The audio starts at its first shown packet's pts plus the samples its edit list (or an iTunSMPB tag) skips,
            // as FFmpeg's start_time, and as Mp4Input's format duration reads it.
            if (start is null && !packet.Discard)
            {
                long skip = packet.SkipSamples;
                if (info.SampleRate > 0 && info.SampleRate != info.TimeBase.Denominator)
                    skip = skip * info.TimeBase.Denominator / info.SampleRate;
                start = Seconds(packet.Pts + skip, info.TimeBase);
            }

            AudioFrame frame;
            try
            {
                frame = decoder.Decode(packet);
            }
            catch (Exception e) when (e is NotSupportedException or InvalidDataException)
            {
                return AudioMeasures.Without($"ManagedCodecs' AAC decoder can't decode this audio: {e.Message}");
            }

            if (frame.SampleCount == 0)
                continue;
            if (measurer is null)
            {
                // The waveform's points: about WaveformPoints of them over the track's length (its packets times the
                // first one's duration).
                var waveform = options.Waveform;
                if (waveform is null)
                {
                    double seconds = info.FrameCount * Seconds(firstDuration, info.TimeBase).TotalSeconds;
                    int samplesPerPixel = (int)Math.Clamp(seconds * frame.SampleRate / options.WaveformPoints, 2, int.MaxValue);
                    waveform = new WaveformOptions { SamplesPerPixel = samplesPerPixel };
                }

                measurer = new Measurer(frame.SampleRate, frame.Channels, start ?? TimeSpan.Zero, waveform, options);
            }
            else if (frame.SampleRate != measurer.SampleRate || frame.Channels != measurer.Channels)
            {
                return AudioMeasures.Without($"The audio changes from {measurer.SampleRate} Hz, {measurer.Channels} channels to {frame.SampleRate} Hz, {frame.Channels}: not measured.");
            }

            measurer.Add(frame);
        }

        return measurer?.Finish() ?? AudioMeasures.Without("The audio track decodes to no samples.");
    }

    static TimeSpan Seconds(long ticks, ManagedCodecs.Rational timeBase) =>
        TimeSpan.FromTicks((long)Math.Round(ticks * (double)timeBase.Numerator / timeBase.Denominator * TimeSpan.TicksPerSecond));

    /// <summary>Everything measured, fed each decoded frame in turn.</summary>
    sealed class Measurer
    {
        readonly TimeSpan _start;
        readonly WaveformBuilder _waveform;
        readonly ReferenceToneFinder _tone;
        readonly SilenceFinder _silence;
        readonly LoudnessMeter _loudness;
        readonly double[] _peaks;
        readonly long[] _fullScale;
        float[] _interleaved = [];
        long _samples;
        int _packetSamples;

        public Measurer(int sampleRate, int channels, TimeSpan start, WaveformOptions waveform, AudioOptions options)
        {
            SampleRate = sampleRate;
            Channels = channels;
            _start = start;
            _waveform = new WaveformBuilder(sampleRate, channels, waveform);
            _tone = new ReferenceToneFinder(sampleRate, channels, options.ReferenceTone);
            _silence = new SilenceFinder(sampleRate, channels, options);
            _loudness = new LoudnessMeter(sampleRate, channels);
            _peaks = new double[channels];
            _fullScale = new long[channels];
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public void Add(AudioFrame frame)
        {
            int count = frame.SampleCount;
            var samples = frame.Samples;
            for (int c = 0; c < Channels; c++)
            {
                double peak = _peaks[c];
                long full = 0;
                foreach (float s in samples[c])
                {
                    float a = Math.Abs(s);
                    if (a > peak)
                        peak = a;
                    if (a >= 1)
                        full++;
                }

                _peaks[c] = peak;
                _fullScale[c] += full;
            }

            _silence.Add(samples, count);
            _loudness.Add(samples, count);

            // FrameReader's builders take the samples interleaved.
            int length = count * Channels;
            if (_interleaved.Length < length)
                _interleaved = new float[length];
            for (int c = 0; c < Channels; c++)
            {
                var channel = samples[c];
                for (int i = 0; i < count; i++)
                    _interleaved[i * Channels + c] = channel[i];
            }

            var interleaved = _interleaved.AsSpan(0, length);
            var time = _start + TimeSpan.FromTicks(_samples * TimeSpan.TicksPerSecond / SampleRate);
            _waveform.Add(interleaved, time);
            if (!_tone.IsComplete)
                _tone.Add(interleaved, time);
            _samples += count;
            _packetSamples = Math.Max(_packetSamples, count);
        }

        public AudioMeasures Finish()
        {
            var loudness = _loudness.Finish();
            var peaks = new ChannelPeak[Channels];
            for (int c = 0; c < Channels; c++)
                peaks[c] = new ChannelPeak(LoudnessMeter.Dbfs(_peaks[c]), LoudnessMeter.Dbfs(_loudness.TruePeak(c)), _fullScale[c]);
            return new AudioMeasures
            {
                Track = new AudioTrack("aac", SampleRate, Channels, _start, _samples, _packetSamples),
                Waveform = _waveform.Finish(),
                ReferenceTone = _tone.Finish(),
                Silences = _silence.Finish(_start),
                Peaks = peaks,
                Loudness = loudness,
            };
        }
    }
}

/// <summary>What <see cref="Mp4Audio.Measure(Stream, AudioOptions?, CancellationToken)"/> measures, and how.</summary>
public sealed record AudioOptions
{
    /// <summary>The waveform's points, as FrameReader's <see cref="WaveformData"/> takes them; null: <see cref="WaveformPoints"/> over the track, channels averaged.</summary>
    public WaveformOptions? Waveform { get; init; }

    /// <summary>About how many points the waveform has when <see cref="Waveform"/> is null.</summary>
    public int WaveformPoints { get; init; } = 1000;

    /// <summary>What counts as reference tone, as ShotDetector's <c>FindReferenceTone</c> takes it; null: 1 kHz, at least 5 s, breaks up to 1 s.</summary>
    public ReferenceToneOptions? ReferenceTone { get; init; }

    /// <summary>Silence is every channel below this level, in dBFS (FFmpeg's silencedetect: -60 dB, 0.001).</summary>
    public double SilenceLevel { get; init; } = -60;

    /// <summary>The shortest silence reported (silencedetect's default: 2 s).</summary>
    public TimeSpan SilenceDuration { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// An MP4 file's sound, measured. Times are on the file's timeline (the container's timestamps, edit list applied), from
/// the audio's first shown sample at <see cref="AudioTrack.Start"/>.
/// </summary>
public sealed record AudioMeasures
{
    /// <summary>Why nothing was measured (no audio track, not AAC, a track ManagedCodecs' decoder refuses); null when measured.</summary>
    public string? NotMeasured { get; init; }

    /// <summary>The decoded track; null when not measured.</summary>
    public AudioTrack? Track { get; init; }

    /// <summary>The waveform: per point, the lowest and highest sample as BBC's audiowaveform computes them (FrameReader's <see cref="WaveformBuilder"/>).</summary>
    public WaveformData? Waveform { get; init; }

    /// <summary>The reference tone's segments (FrameReader's <see cref="ReferenceToneFinder"/>, ShotDetector's definition).</summary>
    public IReadOnlyList<ToneSegment> ReferenceTone { get; init; } = [];

    /// <summary>The silences, as FFmpeg's silencedetect finds them: every channel below <see cref="AudioOptions.SilenceLevel"/> for at least <see cref="AudioOptions.SilenceDuration"/>.</summary>
    public IReadOnlyList<Silence> Silences { get; init; } = [];

    /// <summary>Each channel's peaks, in the decoder's channel order (FL FR FC LFE BL BR for 5.1).</summary>
    public IReadOnlyList<ChannelPeak> Peaks { get; init; } = [];

    /// <summary>Loudness per ITU-R BS.1770-4 and EBU R128; null when not measured.</summary>
    public Loudness? Loudness { get; init; }

    internal static AudioMeasures Without(string reason) => new() { NotMeasured = reason };
}

/// <summary>The audio track as decoded.</summary>
/// <param name="Codec">FFmpeg's name of the codec: "aac".</param>
/// <param name="SampleRate">The decoded samples per second (twice the core's with SBR).</param>
/// <param name="Channels">The decoded channels.</param>
/// <param name="Start">The first shown sample's time on the file's timeline (FFmpeg's start_time of the stream).</param>
/// <param name="Samples">The decoded samples per channel, priming and what the edit list skips left out.</param>
/// <param name="PacketSamples">The samples one packet decodes to (1024 for AAC LC, 2048 with SBR).</param>
public sealed record AudioTrack(string Codec, int SampleRate, int Channels, TimeSpan Start, long Samples, int PacketSamples)
{
    /// <summary>How long the decoded samples last.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Samples * TimeSpan.TicksPerSecond / SampleRate);
}

/// <summary>A silence: from its first silent sample to just after its last.</summary>
public sealed record Silence(TimeSpan Start, TimeSpan End);

/// <summary>One channel's peaks.</summary>
/// <param name="Sample">The largest sample's magnitude, in dBFS.</param>
/// <param name="True">The true peak (the signal between the samples too, oversampled), in dBTP.</param>
/// <param name="FullScale">How many samples reach full scale (a magnitude of 1 or more): clipped, or about to be.</param>
public sealed record ChannelPeak(double Sample, double True, long FullScale);

/// <summary>
/// Silence as FFmpeg's silencedetect (default mode) finds it: a run of instants where every channel's sample is below
/// the level (strictly, in single precision), at least the duration long (rounded to samples); reported from its first
/// instant to the first that isn't silent, or to the end of the audio.
/// </summary>
internal sealed class SilenceFinder(int sampleRate, int channels, AudioOptions options)
{
    readonly float _level = (float)Math.Pow(10, options.SilenceLevel / 20);
    readonly long _shortest = (long)Math.Round(options.SilenceDuration.Ticks * (double)sampleRate / TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero);
    readonly List<(long Start, long End)> _found = [];
    long _position;
    long _run = -1;

    public void Add(float[][] samples, int count)
    {
        for (int i = 0; i < count; i++, _position++)
        {
            bool silent = true;
            for (int c = 0; c < channels && silent; c++)
            {
                float s = samples[c][i];
                silent = s < _level && s > -_level;
            }

            if (silent)
            {
                if (_run < 0)
                    _run = _position;
            }
            else
            {
                End();
            }
        }
    }

    public IReadOnlyList<Silence> Finish(TimeSpan start)
    {
        End();
        return [.. _found.Select(f => new Silence(start + Time(f.Start), start + Time(f.End)))];
    }

    void End()
    {
        if (_run >= 0 && _position - _run >= _shortest)
            _found.Add((_run, _position));
        _run = -1;
    }

    TimeSpan Time(long sample) => TimeSpan.FromTicks(sample * TimeSpan.TicksPerSecond / sampleRate);
}

/// <summary>
/// A stream whose reads, once <see cref="SkipReads"/> is set, read nothing (as Mp4Input's TimingOnlyStream), until
/// <see cref="Fill"/> reads the packet just handed out. ManagedCodecs' MP4 demuxer reads each sample (seeking to it,
/// then reading it whole) just before it hands out its packet, so the audio's data is read and the video's never is.
/// (ManagedCodecs reading only the tracks asked for would make this unnecessary: backlog, "the MP4 index without its
/// copies".)
/// </summary>
internal sealed class AudioDataStream(Stream inner) : Stream
{
    long _seek = -1;
    long _skipped;

    public bool SkipReads { get; set; }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            inner.Position = value;
            _seek = value;
            _skipped = 0;
        }
    }

    /// <summary>The packet just read, its data read from the file now (into its own buffer, which the read skipped).</summary>
    public Packet Fill(Packet packet)
    {
        if (_seek < 0 || _skipped != packet.Data.Length)
            throw new InvalidOperationException("ManagedCodecs' MP4 demuxer no longer reads a packet just before handing it out: Mp4Audio can't skip the video's data.");
        long position = inner.Position;
        inner.Position = _seek;
        inner.ReadExactly(MemoryMarshal.AsMemory(packet.Data).Span);
        inner.Position = position;
        return packet;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!SkipReads)
            return inner.Read(buffer);
        int n = (int)Math.Clamp(Length - Position, 0, buffer.Length);
        inner.Position += n;
        _skipped += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = inner.Seek(offset, origin);
        _seek = position;
        _skipped = 0;
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
