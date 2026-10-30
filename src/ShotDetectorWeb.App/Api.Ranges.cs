using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// A long video across several workers (docs/decisions/2026-10-26-workers.md): the page plans the ranges
/// (<see cref="Ranges"/>), each worker opens the same file and scores one (<see cref="ScoreRange"/>, then
/// <see cref="RangeBytes"/>, the range's picture measures with them), and the page's own worker merges their bytes into
/// the run's result (<see cref="Merge"/>), which <see cref="ScenesCsv"/>, <see cref="StatsCsv"/> and <see cref="Picture"/>
/// then give as for <see cref="Detect"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    static byte[]? _rangeBytes;

    /// <summary>What a failed merge's message starts with: the page then detects again with one worker.</summary>
    const string NotMerged = "The ranges didn't merge: ";

    /// <summary>
    /// The opened file split into at most <paramref name="count"/> ranges at keyframes (<see cref="Mp4Input.Ranges"/>),
    /// as JSON: <c>{ frames, ranges: [{ firstFrame, endFrame }] }</c>, frames the video shows; fewer ranges when the
    /// keyframes are too few. From <paramref name="from"/> (0, or a stopped run's rest's first frame: <see cref="Kept"/>),
    /// the frames from there on.
    /// </summary>
    [JSExport]
    public static string Ranges(int count, int from)
    {
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var ranges = input.Ranges(count, from).Select(r => new RangeResponse(r.FirstFrame, r.EndFrame)).ToArray();
        return JsonSerializer.Serialize(new RangesResponse(input.ShownFrames, ranges), RangesJson.Default.RangesResponse);
    }

    /// <summary>
    /// Scores range <paramref name="index"/> of <see cref="Ranges"/>(<paramref name="count"/>, <paramref name="from"/>) with the page's options, as
    /// <see cref="Detect"/> runs (progress, pauses, <see cref="Stop"/> and <see cref="Cancel"/>): a promise of
    /// <c>{ frames, stopped }</c>, the frames scored from the range's first; its scores' bytes then come from
    /// <see cref="RangeBytes"/>. A stop ends the range where it got to; one before its first frame leaves it empty.
    /// </summary>
    [JSExport]
    public static async Task<string> ScoreRange(string optionsJson, int count, int index, int from)
    {
        _result = null;
        _picture = null;
        _kept = null;
        _rangeBytes = null;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var range = input.Ranges(count, from)[index];
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        _stopping = _stopped = false;
        using var run = _run = new CancellationTokenSource();
        // No OnShot: the merge tells the shots. Since ShotDetector 3.22.0 a range from frame 0 tells its own too, which
        // would tell the first range's twice.
        var options = request.ToOptions(new RangeProgress(range.EndFrame - range.FirstFrame), onShot: null);
        var picture = new PictureMeter(input.Video, range);
        RangeScores? scores = null;
        try
        {
            scores = await DamagedPrefixed(() => input.ScoreRangeAsync(range, PauseEvery, Between, options, run.Token, picture));
        }
        catch (ShotDetectionException) when (_stopped)
        {
            // Stopped before the range's first frame: nothing scored.
        }
        catch (OperationCanceledException e) when (run.IsCancellationRequested)
        {
            throw new RunCancelledException(e);
        }
        finally
        {
            _run = null;
        }

        _rangeBytes = RangePacked(scores?.ToBytes() ?? [], picture.Measures);
        return JsonSerializer.Serialize(new ScoredResponse(scores?.FrameCount ?? 0, _stopped), RangesJson.Default.ScoredResponse);
    }

    /// <summary>
    /// The last <see cref="ScoreRange"/>'s scores as bytes (<see cref="RangeScores.ToBytes"/>) and its picture measures
    /// (<see cref="PictureMeasures.ToBytes"/>), one after the other; empty when it scored none.
    /// </summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static byte[] RangeBytes() => _rangeBytes ?? throw new InvalidOperationException("Score a range first.");

    /// <summary>
    /// Merges the ranges' bytes (<see cref="RangeBytes"/> of every worker's, one after another in
    /// <paramref name="ranges"/>, each <paramref name="lengths"/> long) with the options they were scored with: the ones
    /// that follow each other from the start (<see cref="Mp4Input.FromStart"/>), so a stopped run's result is the one
    /// for the frames from the start without a gap. Answers as <see cref="Detect"/> does, <c>stopped</c> when they don't
    /// reach the video's end, and tells the shots through <see cref="ShotFound"/>. Ranges that don't make up the video's
    /// frames (ShotDetector's <see cref="ShotDetectionException"/>: an overlap frame that differs, damaged bytes; or
    /// damaged picture measures) throw with <see cref="NotMerged"/> in front of the reason. The ranges' picture measures
    /// join from the start the same way (<see cref="PictureMeasures.Join"/>), for the frames the result read. A stopped
    /// run's <paramref name="kept"/> bytes (<see cref="Kept"/>'s; empty for none) go first: with no ranges, the stopped
    /// part's result again; with the ranges of its rest (<see cref="Ranges"/> from its first frame), the earlier ranges
    /// <see cref="Mp4Input.Continue"/> keeps and theirs, the whole run's.
    /// </summary>
    [JSExport]
    public static string Merge(string optionsJson, [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] kept,
        [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] ranges, int[] lengths)
    {
        _result = null;
        _picture = null;
        _kept = null;
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        var request = JsonSerializer.Deserialize(optionsJson, ApiJson.Default.DetectRequest)
            ?? throw new ArgumentException("The options are null.", nameof(optionsJson));
        DetectionResult result;
        IReadOnlyList<RangeScores> merged;
        var pictures = new List<PictureMeasures>();
        try
        {
            var earlier = KeptRun.FromBytes(kept);
            var scores = new List<RangeScores>(earlier.Scores);
            if (earlier.Picture is { } p)
                pictures.Add(p);
            if (lengths.Length > 0 && earlier.Scores.Count > 0)
            {
                // The rest's ranges start where the continuation does: the kept ranges from there on, and their
                // measures, are scored again.
                var go = input.Continue(earlier.Scores);
                scores = [.. go.Earlier];
                pictures = earlier.Picture is { } q && go.Rest is { } rest ? [q.Take(rest.FirstFrame)] : [];
            }

            int at = 0;
            foreach (int length in lengths)
            {
                if (length > 0)
                {
                    var (bytes, picture) = RangeUnpacked(ranges.AsMemory(at, length));
                    scores.Add(RangeScores.FromBytes(bytes.Span));
                    pictures.Add(picture);
                }

                at += length;
            }

            merged = Mp4Input.FromStart(scores);
            result = input.Merge(merged, request.ToOptions(new JsProgress(), new JsShots()));
        }
        catch (Exception e) when (e is ShotDetectionException or InvalidDataException)
        {
            throw new InvalidOperationException(NotMerged + e.Message, e);
        }

        return Answered(input, result, merged, PictureMeasures.Join(pictures));
    }

    /// <summary>A range's progress: its frames scored and its own frame count (ScoreRange's expected count is 0).</summary>
    sealed class RangeProgress(int frames) : IProgress<DetectionProgress>
    {
        public void Report(DetectionProgress value) => Progress(value.FramesProcessed, frames);
    }
}

/// <summary><see cref="Api.Ranges"/>' answer: the frames the video shows, and the ranges.</summary>
sealed record RangesResponse(int Frames, IReadOnlyList<RangeResponse> Ranges);

/// <summary>A range: its first frame and the frame after its last (<see cref="FrameRange"/>).</summary>
sealed record RangeResponse(int FirstFrame, int EndFrame);

/// <summary><see cref="Api.ScoreRange"/>'s answer: frames scored from the range's first, and whether it was stopped.</summary>
sealed record ScoredResponse(int Frames, bool Stopped);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RangesResponse))]
[JsonSerializable(typeof(ScoredResponse))]
partial class RangesJson : JsonSerializerContext;
