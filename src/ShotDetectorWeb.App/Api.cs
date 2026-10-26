using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// What the page's worker (wwwroot/worker.js) calls, by name: open a file, detect on it. Strings cross the boundary, JSON
/// made by ShotDetector where it has a form for it (docs/decisions/2026-10-24-the-page.md, "Where the runtime lives").
/// The other exports are partial files of their own, one per feature (Api.Csv.cs, ...; decision 2026-10-25-one-page.md),
/// each with its own JSON context if it needs one, so features are added side by side.
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
    /// shots (<see cref="Shots.Json"/>), the frame count, frame rate and duration, and whether it was stopped; scenedetect's
    /// scene and stats CSVs come from <see cref="ScenesCsv"/> and <see cref="StatsCsv"/>. Reports progress through
    /// <see cref="Progress"/> while it runs, and each shot through <see cref="ShotFound"/> as soon as ShotDetector knows it
    /// (<see cref="DetectionOptions.OnShot"/>). A promise to the worker: every <see cref="PauseEvery"/> frames the run
    /// waits for the worker's next task (<see cref="Pause"/>), so <see cref="Stop"/> and <see cref="Cancel"/> can come in.
    /// </summary>
    [JSExport]
    public static async Task<string> Detect(string optionsJson)
    {
        _result = null;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        _stopping = _stopped = false;
        using var run = _run = new CancellationTokenSource();
        DetectionResult result;
        try
        {
            result = await DamagedPrefixed(() =>
                input.DetectAsync(PauseEvery, Between, request.ToOptions(new JsProgress(), new JsShots()), run.Token));
        }
        catch (OperationCanceledException e) when (run.IsCancellationRequested)
        {
            // A cancelled task never settles the promise JavaScript holds (.NET 10's interop, seen 2026-10-26): a failed
            // one rejects it.
            throw new RunCancelledException(e);
        }
        finally
        {
            _run = null;
        }

        _result = result;
        using var shots = JsonDocument.Parse(Shots.Json(result.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(shots.RootElement, result.FrameCount, result.Video.FrameRate, result.Video.Duration, _stopped),
            ApiJson.Default.DetectResponse);
    }

    /// <summary>
    /// Ends the running <see cref="Detect"/>'s frames at its next pause, as if the file ended there: it then gives the
    /// result for the frames read (its shots, both CSVs), with <c>stopped</c> true. Nothing when no run is going.
    /// </summary>
    [JSExport]
    public static void Stop()
    {
        if (_run is not null)
            _stopping = true;
    }

    /// <summary>
    /// Abandons the running <see cref="Detect"/> at its next pause: it fails with an
    /// <see cref="OperationCanceledException"/> and keeps no result. Nothing when no run is going.
    /// </summary>
    [JSExport]
    public static void Cancel() => _run?.Cancel();

    /// <summary>
    /// Frames between two pauses. Each pause costs a round trip through the worker's event loop; at 8 frames that is
    /// well under 1% of a run on the samples and the Sintel trailer (README "Stopping a run"), and a stop waits at most
    /// 8 frames.
    /// </summary>
    const int PauseEvery = 8;

    static CancellationTokenSource? _run;
    static bool _stopping, _stopped;

    static async ValueTask<bool> Between(int framesRead)
    {
        await Pause(framesRead);
        _stopped = _stopping;
        return !_stopping;
    }

    /// <summary>The last <see cref="Detect"/>'s result, for the exports that show more of it.</summary>
    static DetectionResult Result => _result ?? throw new InvalidOperationException("Detect first.");

    /// <summary>ManagedCodecs' <see cref="InvalidDataException"/> (a damaged file) with the page's words in front.</summary>
    static T DamagedPrefixed<T>(Func<T> f)
    {
        try { return f(); }
        catch (InvalidDataException e) { throw new InvalidDataException(Damaged + e.Message, e); }
    }

    /// <summary><see cref="DamagedPrefixed{T}(Func{T})"/> for a run that fails in its task.</summary>
    static async Task<T> DamagedPrefixed<T>(Func<Task<T>> f)
    {
        try { return await f(); }
        catch (InvalidDataException e) { throw new InvalidDataException(Damaged + e.Message, e); }
    }

    /// <summary>Frames done and expected (0 when unknown), about ten times a second while <see cref="Detect"/> runs.</summary>
    [JSImport("progress", "host")]
    internal static partial void Progress(int done, int expected);

    /// <summary>
    /// Resolves in the worker's next task (a <c>MessageChannel</c> message, wwwroot/pause.js), so the messages that came
    /// in meanwhile run first: <see cref="Detect"/>'s pause, told how many frames it has read.
    /// </summary>
    [JSImport("pause", "host")]
    internal static partial Task Pause(int framesRead);

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

/// <summary><see cref="Api.Detect"/> was cancelled (<see cref="Api.Cancel"/>): no result.</summary>
sealed class RunCancelledException(OperationCanceledException inner) : Exception("The run was cancelled.", inner);

/// <summary>
/// What <see cref="Api.Detect"/> returns. Stopped: the run was stopped (<see cref="Api.Stop"/>) and its frames ended at
/// <c>FrameCount</c>, so the shots and CSVs are the ones for that part of the video; Duration is still the file's.
/// </summary>
sealed record DetectResponse(JsonElement Shots, int FrameCount, Fps FrameRate, TimeSpan? Duration, bool Stopped);

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
