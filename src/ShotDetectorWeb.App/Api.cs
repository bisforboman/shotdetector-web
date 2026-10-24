using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// What the page's worker (wwwroot/worker.js) calls: open a file, detect on it. Strings cross the boundary, JSON made
/// by ShotDetector where it has a form for it (docs/decisions/2026-10-24-the-page.md, "Where the runtime lives").
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    static Mp4Input? _input;

    /// <summary>Opens an MP4 for <see cref="Detect"/>: its <see cref="YuvVideo"/> as JSON, or the library's exception.</summary>
    [JSExport]
    public static string Open(byte[] file)
    {
        _input = null;
        var input = Mp4Input.Open(file);
        _input = input;
        return JsonSerializer.Serialize(input.Video, ApiJson.Default.YuvVideo);
    }

    /// <summary>
    /// Detects on the opened file with the page's options (<see cref="DetectRequest"/>), stats always collected: the
    /// shots (<see cref="Shots.Json"/>), scenedetect's scene and stats CSVs, the frame count, frame rate and duration.
    /// Reports progress through <see cref="Progress"/> while it runs.
    /// </summary>
    [JSExport]
    public static string Detect(string optionsJson)
    {
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        var result = input.Detect(request.ToOptions(new JsProgress()));
        using var shots = JsonDocument.Parse(Shots.Json(result.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(
                shots.RootElement,
                Shots.Csv(result.Shots, cuts: result.Cuts),
                result.Stats!.Csv(result.Video.Position),
                result.FrameCount,
                result.Video.FrameRate,
                result.Video.Duration),
            ApiJson.Default.DetectResponse);
    }

    /// <summary>The names of the assemblies loaded so far, sorted: the checks show nothing of FFmpeg's is loaded.</summary>
    [JSExport]
    public static string[] LoadedAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Order(StringComparer.Ordinal).ToArray();

    /// <summary>Frames done and expected (0 when unknown), about ten times a second while <see cref="Detect"/> runs.</summary>
    [JSImport("progress", "host")]
    internal static partial void Progress(int done, int expected);

    sealed class JsProgress : IProgress<DetectionProgress>
    {
        public void Report(DetectionProgress value) => Progress(value.FramesProcessed, value.ExpectedFrames);
    }
}

/// <summary>
/// The options the page offers: the detector and its threshold (null: the detector's default), the minimum shot length
/// in scenedetect's forms (null: its default, "0.6s"), or several detectors in one run.
/// </summary>
sealed record DetectRequest
{
    public DetectorKind Detector { get; init; } = DetectorKind.Adaptive;
    public double? Threshold { get; init; }
    public string? MinSceneLength { get; init; }
    public IReadOnlyList<DetectorRequest>? Detectors { get; init; }

    public DetectionOptions ToOptions(IProgress<DetectionProgress> progress)
    {
        var options = new DetectionOptions
        {
            Detector = Detector,
            Threshold = Threshold,
            Detectors = Detectors?.Select(d => new DetectorSettings(d.Kind) { Threshold = d.Threshold }).ToArray(),
            CollectStats = true,
            Progress = progress,
        };
        return MinSceneLength is null ? options : options with { MinSceneLength = MinSceneLength };
    }
}

/// <summary>One detector of a run with several.</summary>
sealed record DetectorRequest(DetectorKind Kind, double? Threshold = null);

sealed record DetectResponse(JsonElement Shots, string ScenesCsv, string StatsCsv, int FrameCount, Fps FrameRate, TimeSpan? Duration);

/// <summary>Durations as seconds, which JavaScript can use as they are.</summary>
sealed class SecondsConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TimeSpan.FromSeconds(reader.GetDouble());

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.TotalSeconds);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = [typeof(SecondsConverter)])]
[JsonSerializable(typeof(YuvVideo))]
[JsonSerializable(typeof(DetectRequest))]
[JsonSerializable(typeof(DetectResponse))]
partial class ApiJson : JsonSerializerContext;
