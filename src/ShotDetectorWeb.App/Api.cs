using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FrameReader;
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
    static VideoInput? _input;
    static JSObject? _file;
    static DetectionResult? _result;

    /// <summary>What a damaged file's message starts with on the page (decision "A damaged file's message").</summary>
    const string Damaged = "The file is damaged or not a video this page can read: ";

    /// <summary>
    /// Opens an MP4 or an AVI for <see cref="Detect"/>: its <see cref="YuvVideo"/> as JSON with the frames it shows
    /// (<see cref="VideoInput.ShownFrames"/>, what a whole run reads, where <c>frameCount</c> is FFmpeg's nb_frames: null in a
    /// fragmented file, the track's samples past an edit list's end) as <c>frames</c>, and its audio track
    /// (<see cref="VideoInput.Audio"/>) as <c>audio</c>, <c>{ codec, sampleRate, channels }</c> or null without one; or the
    /// library's exception (a damaged file's prefixed with <see cref="Damaged"/>). The file is a JavaScript <c>Blob</c> (the picked <c>File</c>), read
    /// in slices as the library needs them (<see cref="BlobStream"/>), never copied in whole.
    /// </summary>
    [JSExport]
    public static string Open(JSObject file)
    {
        _input = null;
        _result = null;
        _picture = null;
        _toldInFirstRange = null;
        _file?.Dispose();
        _file = file;
        return Opened(DamagedPrefixed(() => VideoInput.Open(() => new BlobStream(file))));
    }

    /// <summary><see cref="Open"/>'s JSON of the file just opened, which <see cref="Detect"/> then runs on.</summary>
    static string Opened(VideoInput input)
    {
        _input = input;
        var json = JsonSerializer.SerializeToNode(input.Video, ApiJson.Default.YuvVideo)!.AsObject();
        json["frames"] = input.ShownFrames;
        json["audio"] = input.Audio is { } a
            ? new JsonObject { ["codec"] = a.Codec, ["sampleRate"] = a.SampleRate, ["channels"] = a.Channels }
            : null;
        return json.ToJsonString();
    }

    /// <summary>
    /// Detects on the opened file with the page's options (<see cref="DetectRequest"/>), stats always collected: the
    /// shots (<see cref="Shots.Json"/>), the frame count, frame rate and duration, whether it was stopped, and
    /// ShotDetector's warnings; scenedetect's scene and stats CSVs come from <see cref="ScenesCsv"/> and
    /// <see cref="StatsCsv"/>. Reports progress through <see cref="Progress"/> while it runs, and each shot through
    /// <see cref="ShotFound"/> as soon as ShotDetector knows it
    /// (<see cref="DetectionOptions.OnShot"/>). A promise to the worker: every <see cref="PauseEvery"/> frames the run
    /// waits for the worker's next task (<see cref="Pause"/>), so <see cref="Stop"/> and <see cref="Cancel"/> can come in.
    /// The video is scored as one range from frame 0 and merged (ShotDetector's ScoreRange and Merge: Detect's result
    /// byte for byte), so that a stopped run leaves its scores (<see cref="Kept"/>) for <see cref="Resume"/>.
    /// </summary>
    [JSExport]
    public static Task<string> Detect(string optionsJson) => Continued(optionsJson, KeptRun.None);

    /// <summary>
    /// <see cref="Detect"/> on the opened file going on from <paramref name="earlier"/>'s scores
    /// (<see cref="VideoInput.Continue"/>), the rest of the video scored from the place a range can start before where they
    /// end, each shot of the rest told as <see cref="Detect"/> tells them, and the result the whole run's.
    /// </summary>
    static async Task<string> Continued(string optionsJson, KeptRun earlier)
    {
        _result = null;
        _picture = null;
        _kept = null;
        _toldInFirstRange = null;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        _stopping = _stopped = false;
        using var run = _run = new CancellationTokenSource();
        var go = input.Continue(earlier.Scores, request.ToOptions());
        var picture = go.Rest is { } planned ? new PictureMeter(input.Video, planned) : null;
        var options = request.ToOptions(new JsProgress(go.Rest?.FirstFrame ?? 0, input.ShownFrames), new JsShots());

        DetectionResult result;
        IReadOnlyList<RangeScores> ranges;
        try
        {
            (result, ranges) = await Host.Run(() => DamagedPrefixed(async () =>
            {
                var scored = go.Earlier.ToList();
                if (go.Rest is { } rest)
                {
                    try
                    {
                        scored.Add(await input.ScoreRangeAsync(go.Earlier, rest, PauseEvery, Between, options, run.Token, picture));
                    }
                    catch (ShotDetectionException) when (_stopped && scored.Count > 0)
                    {
                        // Stopped before the rest's first frame: the earlier ranges are the result.
                    }
                }

                var merged = input.FromStart(scored);
                // The shots were told as the frames were scored, the merge's would be told twice.
                return (input.Merge(merged, options with { OnShot = null, Progress = null }), merged);
            }));
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

        var earlierPicture = go.Earlier.Count == 0 ? null : earlier.Picture?.Take(go.Rest?.FirstFrame ?? int.MaxValue);
        return Answered(input, options, result, ranges,
            PictureMeasures.Join(new[] { earlierPicture, picture?.Measures }.OfType<PictureMeasures>()));
    }

    /// <summary>
    /// A run's result kept for the exports that show more of it (<see cref="Picture"/> for the frames it read,
    /// <see cref="Kept"/> its ranges), and its answer as JSON (<see cref="DetectResponse"/>).
    /// </summary>
    static string Answered(VideoInput input, DetectionOptions options, DetectionResult result, IReadOnlyList<RangeScores> ranges,
        PictureMeasures? picture)
    {
        _result = result;
        _picture = picture?.Take(result.FrameCount);
        _kept = new KeptRun(ranges, _picture);
        _keptOptions = options;
        using var shots = JsonDocument.Parse(Shots.Json(result.Shots));
        return JsonSerializer.Serialize(
            new DetectResponse(shots.RootElement, result.FrameCount, result.Video.FrameRate, result.Video.Duration,
                result.FrameCount < input.ShownFrames, result.Warnings),
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
    // Set by Stop on the JavaScript thread, read between frames by the run (on the thread pool with threads, Host).
    static volatile bool _stopping, _stopped;

    static async ValueTask<bool> Between(int framesRead)
    {
        await Host.Call(() => Pause(framesRead));
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

    /// <summary>
    /// A run's progress as frames of the video: a range's frames scored (counted from its first frame,
    /// <paramref name="first"/>) and the frames the video shows (a range expects none of its own).
    /// </summary>
    sealed class JsProgress(int first = 0, int frames = 0) : IProgress<DetectionProgress>
    {
        public void Report(DetectionProgress value) =>
            Host.Post(() => Progress(first + value.FramesProcessed, frames > 0 ? frames : value.ExpectedFrames));
    }

    // Not Progress<T>: that would post to a synchronization context and could tell a shot after Detect returned.
    // told: each shot's JSON added as it is told (a split run's first range's, for Merge: MergedShots).
    sealed class JsShots(List<string>? told = null) : IProgress<Shot>
    {
        public void Report(Shot value)
        {
            string shot = ShotJson(value);
            told?.Add(shot);
            Host.Post(() => ShotFound(shot));
        }
    }

    /// <summary>One shot as an object of <see cref="Shots.Json"/>'s array: what <see cref="ShotFound"/> tells.</summary>
    static string ShotJson(Shot value)
    {
        using var json = JsonDocument.Parse(Shots.Json([value]));
        return json.RootElement[0].GetRawText();
    }
}

/// <summary>
/// The options the page offers: the detector and its threshold (null: the detector's default), the minimum shot length
/// in scenedetect's forms (null: its default, "0.6s"), the adaptive detector's window (<c>frameWindow</c>: frames on
/// each side averaged, scenedetect's <c>window_width</c>; null: 2) and minimum content_val for a cut
/// (<c>minContentVal</c>, scenedetect's <c>min_content_val</c>; null: 15), or several detectors in one run.
/// </summary>
sealed record DetectRequest
{
    public DetectorKind Detector { get; init; } = DetectorKind.Adaptive;
    public double? Threshold { get; init; }
    public string? MinSceneLength { get; init; }
    public int? FrameWindow { get; init; }
    public double? MinContentVal { get; init; }
    public IReadOnlyList<DetectorRequest>? Detectors { get; init; }
    public DeinterlaceMode? Deinterlace { get; init; }

    public DetectionOptions ToOptions(IProgress<DetectionProgress>? progress = null, IProgress<Shot>? onShot = null)
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
        if (MinSceneLength is not null)
            options = options with { MinSceneLength = MinSceneLength };
        if (FrameWindow is { } window)
            options = options with { FrameWindow = window };
        if (MinContentVal is { } minContentVal)
            options = options with { MinContentVal = minContentVal };
        if (Deinterlace is { } deinterlace)
            options = options with { Deinterlace = deinterlace };
        return options;
    }
}

/// <summary>One detector of a run with several.</summary>
sealed record DetectorRequest(DetectorKind Kind, double? Threshold = null);

/// <summary><see cref="Api.Detect"/> was cancelled (<see cref="Api.Cancel"/>): no result.</summary>
sealed class RunCancelledException(OperationCanceledException inner) : Exception("The run was cancelled.", inner);

/// <summary>
/// What <see cref="Api.Detect"/> returns. Stopped: the run was stopped (<see cref="Api.Stop"/>) and its frames ended at
/// <c>FrameCount</c>, so the shots and CSVs are the ones for that part of the video; Duration is still the file's.
/// Warnings: <see cref="DetectionResult.Warnings"/>' sentences as ShotDetector words them (empty when there are none),
/// what the user should know about how the video was read.
/// </summary>
sealed record DetectResponse(
    JsonElement Shots, int FrameCount, Fps FrameRate, TimeSpan? Duration, bool Stopped, IReadOnlyList<string> Warnings);

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
