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
    static JSObject? _file;
    static DetectionResult? _result;

    /// <summary>What a damaged file's message starts with on the page (decision "A damaged file's message").</summary>
    const string Damaged = "The file is damaged or not an MP4 this page can read: ";

    /// <summary>
    /// Opens an MP4 for <see cref="Detect"/>: its <see cref="YuvVideo"/> as JSON, or the library's exception (a damaged
    /// file's prefixed with <see cref="Damaged"/>). The file is a JavaScript <c>Blob</c> (the picked <c>File</c>), read
    /// in slices as the library needs them (<see cref="BlobStream"/>), never copied in whole.
    /// </summary>
    [JSExport]
    public static string Open(JSObject file)
    {
        _input = null;
        _result = null;
        _file?.Dispose();
        _file = file;
        var input = DamagedPrefixed(() => Mp4Input.Open(() => new BlobStream(file)));
        _input = input;
        return JsonSerializer.Serialize(input.Video, ApiJson.Default.YuvVideo);
    }

    /// <summary>
    /// Detects on the opened file with the page's options (<see cref="DetectRequest"/>), stats always collected: the
    /// shots (<see cref="Shots.Json"/>), the frame count, frame rate and duration; scenedetect's scene and stats CSVs come
    /// from <see cref="ScenesCsv"/> and <see cref="StatsCsv"/>. Reports progress through <see cref="Progress"/> while it
    /// runs, and each shot through <see cref="ShotFound"/> as soon as ShotDetector knows it
    /// (<see cref="DetectionOptions.OnShot"/>).
    /// </summary>
    [JSExport]
    public static string Detect(string optionsJson)
    {
        _result = null;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        var result = DamagedPrefixed(() => input.Detect(request.ToOptions(new JsProgress(), new JsShots())));
        _result = result;
        using var shots = JsonDocument.Parse(Shots.Json(result.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(shots.RootElement, result.FrameCount, result.Video.FrameRate, result.Video.Duration),
            ApiJson.Default.DetectResponse);
    }

    /// <summary>
    /// The last <see cref="Detect"/>'s scene list CSV (scenedetect list-scenes'), a string as it is: not inside the JSON,
    /// where escaping a film's stats CSV took buffers of six times its length that the heap kept (README "Large files").
    /// </summary>
    [JSExport]
    public static string ScenesCsv() => Shots.Csv(Result.Shots, cuts: Result.Cuts);

    /// <summary>The last <see cref="Detect"/>'s stats CSV (scenedetect -s's), a string as it is (<see cref="ScenesCsv"/>).</summary>
    [JSExport]
    public static string StatsCsv() => Result.Stats!.Csv(Result.Video.Position);

    static DetectionResult Result => _result ?? throw new InvalidOperationException("Detect first.");

    /// <summary>The names of the assemblies loaded so far, sorted: the checks show nothing of FFmpeg's is loaded.</summary>
    [JSExport]
    public static string[] LoadedAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Order(StringComparer.Ordinal).ToArray();

    /// <summary>ManagedCodecs' <see cref="InvalidDataException"/> (a damaged file) with the page's words in front.</summary>
    static T DamagedPrefixed<T>(Func<T> f)
    {
        try { return f(); }
        catch (InvalidDataException e) { throw new InvalidDataException(Damaged + e.Message, e); }
    }

    /// <summary>Frames done and expected (0 when unknown), about ten times a second while <see cref="Detect"/> runs.</summary>
    [JSImport("progress", "host")]
    internal static partial void Progress(int done, int expected);

    /// <summary>
    /// A shot as soon as ShotDetector knows it, while <see cref="Detect"/> runs: one object of
    /// <see cref="Shots.Json"/>'s array, so it reads as the result's shots do. With one detector the page sends, a few
    /// frames after the cut that ends it; with several, all of them at the end (<see cref="DetectionOptions.OnShot"/>).
    /// </summary>
    [JSImport("shot", "host")]
    internal static partial void ShotFound(string shotJson);

    sealed class JsProgress : IProgress<DetectionProgress>
    {
        public void Report(DetectionProgress value) => Progress(value.FramesProcessed, value.ExpectedFrames);
    }

    // Not Progress<T>: that would post to a synchronization context and could tell a shot after Detect returned.
    sealed class JsShots : IProgress<Shot>
    {
        public void Report(Shot value)
        {
            using var json = JsonDocument.Parse(Shots.Json([value]));
            ShotFound(json.RootElement[0].GetRawText());
        }
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

    public DetectionOptions ToOptions(IProgress<DetectionProgress> progress, IProgress<Shot> onShot)
    {
        var options = new DetectionOptions
        {
            Detector = Detector,
            Threshold = Threshold,
            Detectors = Detectors?.Select(d => new DetectorSettings(d.Kind) { Threshold = d.Threshold }).ToArray(),
            CollectStats = true,
            Progress = progress,
            OnShot = onShot,
        };
        return MinSceneLength is null ? options : options with { MinSceneLength = MinSceneLength };
    }
}

/// <summary>One detector of a run with several.</summary>
sealed record DetectorRequest(DetectorKind Kind, double? Threshold = null);

sealed record DetectResponse(JsonElement Shots, int FrameCount, Fps FrameRate, TimeSpan? Duration);

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
