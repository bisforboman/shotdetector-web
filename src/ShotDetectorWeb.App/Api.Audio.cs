using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameReader;

namespace ShotDetectorWeb.App;

/// <summary>
/// The sound: the opened file's audio measured in one pass with ManagedCodecs' AAC decoder (<see cref="Mp4Audio"/>),
/// its own export, called by the page after <see cref="Detect"/> so detection's time and results stay as they are
/// (docs/decisions/2026-10-24-audio.md).
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The opened file's <see cref="AudioMeasures"/> as JSON (<see cref="AudioResponse"/>), read through a
    /// <see cref="BlobStream"/> of its own as <see cref="Open"/> reads the file. A file without an audio track, audio
    /// that isn't AAC, or audio the decoder refuses gives <c>notMeasured</c> saying why, the core's words as they are
    /// (the page shows nothing for "The file has no audio track."); a damaged file throws, its message prefixed as Open's.
    /// </summary>
    [JSExport]
    public static string Audio()
    {
        var file = _input is null ? null : _file;
        if (file is null)
            throw new InvalidOperationException("Open a file first.");
        var measures = DamagedPrefixed(() => Mp4Audio.Measure(new BlobStream(file)));
        return JsonSerializer.Serialize(AudioResponse.From(measures), AudioJson.Default.AudioResponse);
    }
}

/// <summary>
/// <see cref="AudioMeasures"/> for the page: times in seconds, levels in dBFS (dBTP for true peaks, LUFS and LU for
/// loudness), a level of no signal at all (minus infinity) as null.
/// </summary>
sealed record AudioResponse(
    string? NotMeasured,
    AudioTrackResponse? Track,
    WaveformResponse? Waveform,
    IReadOnlyList<ToneSegment> ReferenceTone,
    IReadOnlyList<Silence> Silences,
    IReadOnlyList<ChannelPeak> Peaks,
    Loudness? Loudness)
{
    public static AudioResponse From(AudioMeasures m) => new(
        m.NotMeasured,
        m.Track is { } t ? new AudioTrackResponse(t.Codec, t.SampleRate, t.Channels, t.Start, t.Duration) : null,
        m.Waveform is { } w && m.Track is { } track ? WaveformResponse.From(w, track.Start) : null,
        m.ReferenceTone,
        m.Silences,
        m.Peaks,
        m.Loudness);
}

/// <summary>The decoded track: FFmpeg's codec name, its decoded rate and channels, where it starts and how long it lasts.</summary>
sealed record AudioTrackResponse(string Codec, int SampleRate, int Channels, TimeSpan Start, TimeSpan Duration);

/// <summary>
/// The waveform (FrameReader's <see cref="WaveformData"/>, BBC audiowaveform's points): point <c>i</c> covers the
/// <see cref="SamplesPerPoint"/> samples from <c>start + i / pointsPerSecond</c> seconds; <see cref="Min"/> and
/// <see cref="Max"/> hold, per waveform (one, the channels averaged, unless split), each point's lowest and highest
/// sample as audiowaveform's 16-bit values (full scale 32767).
/// </summary>
sealed record WaveformResponse(
    TimeSpan Start, int SampleRate, int SamplesPerPoint, double PointsPerSecond, int Channels, short[][] Min, short[][] Max)
{
    public static WaveformResponse From(WaveformData w, TimeSpan start)
    {
        var min = new short[w.Channels][];
        var max = new short[w.Channels][];
        for (int c = 0; c < w.Channels; c++)
        {
            min[c] = new short[w.Length];
            max[c] = new short[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                min[c][i] = w.Min(c, i);
                max[c][i] = w.Max(c, i);
            }
        }

        return new(start, w.SampleRate, w.SamplesPerPixel, w.SampleRate / (double)w.SamplesPerPixel, w.Channels, min, max);
    }
}

/// <summary>A level of no signal at all (minus infinity dBFS: a silent channel's peak), which JSON has no number for, as null.</summary>
sealed class FiniteOrNullConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? double.NegativeInfinity : reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value))
            writer.WriteNumberValue(value);
        else
            writer.WriteNullValue();
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(SecondsConverter), typeof(FiniteOrNullConverter)])]
[JsonSerializable(typeof(AudioResponse))]
partial class AudioJson : JsonSerializerContext;
