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
    /// without an audio track, a track that isn't AAC, an AVI's audio, or one ManagedCodecs' decoder refuses gives measures with
    /// <see cref="AudioMeasures.NotMeasured"/> saying so; a damaged file throws ManagedCodecs' <see cref="InvalidDataException"/>.
    /// </summary>
    public static AudioMeasures Measure(Stream stream, AudioOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new AudioOptions();
        // An AVI's audio isn't decoded (ManagedCodecs' AVI demuxer gives no audio): said so, as for audio that isn't AAC.
        if (AviHeader.IsAvi(stream))
        {
            var avi = AviHeader.Read(stream);
            stream.Position = 0;
            return avi.Audio.FirstOrDefault() is { } track
                ? AudioMeasures.Without($"The audio is '{track.Codec}' in an AVI file: only AAC in MP4 is measured.")
                : AudioMeasures.Without("The file has no audio track.");
        }

        // The audio track alone: the video's samples are neither indexed nor read.
        using var demuxer = new Mp4Demuxer(stream, MediaTypes.Audio, leaveOpen: true);
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

        Measurer? measurer = null;
        TimeSpan? start = null;
        long firstDuration = 0;
        foreach (var packet in demuxer.ReadPackets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (firstDuration == 0)
                firstDuration = packet.Duration;

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

            // The audio starts at the pts of the first shown packet that decodes to samples, plus the samples its edit
            // list (or an iTunSMPB tag) skips: FFmpeg's first decoded frame's time, which its filters count from. With
            // an edit list that is its start_time; without one (Big Buck Bunny's 2008 MP4, the real-world check) the
            // decoder's first packet gives no samples, in FFmpeg too, and the audio starts at the second's pts.
            if (start is null && !packet.Discard)
            {
                long skip = packet.SkipSamples;
                if (info.SampleRate > 0 && info.SampleRate != info.TimeBase.Denominator)
                    skip = skip * info.TimeBase.Denominator / info.SampleRate;
                start = Seconds(packet.Pts + skip, info.TimeBase);
            }
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

    /// <summary>EBU R128's absolute gate, in LUFS: an integrated loudness at it is ebur128's for nothing above it.</summary>
    const double AbsoluteGate = -70;

    /// <summary>
    /// BS.1770-4's channel weights in ManagedCodecs' AAC output order (FL FR FC LFE BL BR, configuration 7 then SL SR; four
    /// channels FL FR FC BC; three FC FL FR, all front): 1 for the front, 0 for the LFE, 1.41 for the rest, as FFmpeg's
    /// ebur128 weighs them. Not FrameReader's <see cref="ChannelWeights.Default"/>, which is FFmpeg's default layout (three
    /// channels are 2.1 there, an LFE third).
    /// </summary>
    static ChannelWeights Weights(int channels) =>
        new(Enumerable.Range(0, channels).Select(c => c < 3 ? 1 : c == 3 && channels is 6 or 8 ? 0 : 1.41));

    /// <summary>Everything measured, fed each decoded frame in turn.</summary>
    sealed class Measurer
    {
        readonly TimeSpan _start;
        readonly WaveformBuilder _waveform;
        readonly ReferenceToneFinder _tone;
        readonly SilenceFinder _silence;
        readonly LoudnessMeter _loudness;
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
            _loudness = new LoudnessMeter(sampleRate, channels, Weights(channels));
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public void Add(AudioFrame frame)
        {
            int count = frame.SampleCount;
            var samples = frame.Samples;
            _silence.Add(samples, count);

            // FrameReader's builders and loudness meter take the samples interleaved.
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
            _loudness.Add(interleaved);
            var time = _start + TimeSpan.FromTicks(_samples * TimeSpan.TicksPerSecond / SampleRate);
            _waveform.Add(interleaved, time);
            if (!_tone.IsComplete)
                _tone.Add(interleaved, time);
            _samples += count;
            _packetSamples = Math.Max(_packetSamples, count);
        }

        public AudioMeasures Finish()
        {
            // FrameReader's LoudnessMeter (ebur128's arithmetic, BS.1770-4 Annex 2's true peak): its integrated loudness
            // is ebur128's -70 LUFS when no block passes the absolute gate, which the page tells as none.
            var measured = _loudness.Finish();
            var l = measured.Loudness;
            var loudness = new Loudness(l.Integrated <= AbsoluteGate ? null : l.Integrated, l.Range, l.TruePeak);
            var peaks = measured.Channels.Select(c => new ChannelPeak(c.SamplePeak, c.TruePeak, c.FullScale)).ToArray();
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

/// <summary>
/// Loudness as ITU-R BS.1770-4 and EBU Tech 3341/3342 define it: integrated loudness in LUFS, loudness range in LU, and
/// true peak in dBTP, by FrameReader's <see cref="LoudnessMeter"/> (ebur128's arithmetic, without FFmpeg). FFmpeg's
/// <c>ebur128</c> filter (with <c>peak=true</c>) measures the same; NativeCheck compares the two (README, Checks).
/// </summary>
/// <param name="Integrated">The integrated loudness (gated, LUFS); null when nothing is louder than the absolute gate (-70 LUFS).</param>
/// <param name="Range">The loudness range (LU): the spread of the short-term loudness, from its 10th to its 95th percentile; 0 when there is none.</param>
/// <param name="TruePeak">The highest true peak of any channel (dBTP).</param>
public sealed record Loudness(double? Integrated, double Range, double TruePeak);

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
