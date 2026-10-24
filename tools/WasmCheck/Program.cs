// ShotDetectorWeb.Core on WebAssembly: every run of tools/Shared/Runs.cs on each sample, its scene and stats CSVs written
// to the output folder under the names tools/NativeCheck gives its own (tools/check.sh compares the two folders). Fails
// if the runs load an assembly of FFmpeg's (FFmpeg.AutoGen); says whether one was loaded at all (interpreted: none).
//   node main.mjs <out dir> <sample.mp4>...   (from the AppBundle directory; files are read and written through Node)
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using ShotDetectorWeb;
using ShotDetectorWeb.Checks;

string outDir = args[0];
Console.Error.WriteLine($"Vector128 accelerated: {Vector128.IsHardwareAccelerated}, processors: {Environment.ProcessorCount}");
var atStart = Ffmpeg();
foreach (string sample in args.Skip(1))
{
    var input = Mp4Input.Open(Convert.FromBase64String(Host.ReadFile(sample)));
    foreach (var (run, options) in Runs.All)
    {
        var watch = Stopwatch.StartNew();
        var result = input.Detect(options);
        var (scenes, stats) = Runs.Csvs(result);
        string name = $"{outDir}/{Runs.BaseName(sample, run)}";
        Host.WriteFile(name + ".scenes.csv", scenes);
        Host.WriteFile(name + ".stats.csv", stats);
        Console.Error.WriteLine($"{Path.GetFileName(sample)} {run}: {result.FrameCount} frames, {result.Shots.Count} shots, {watch.Elapsed.TotalSeconds:F2} s");
    }
}

Console.Error.WriteLine($"Loaded assemblies: {string.Join(", ", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order())}");
var byRuns = Ffmpeg().Except(atStart).ToList();
if (byRuns.Count > 0)
{
    Console.Error.WriteLine($"FFmpeg assemblies loaded by the runs: {string.Join(", ", byRuns)}");
    return 1;
}

// The AOT runtime loads some assemblies before Main that the interpreter never touches, FFmpeg.AutoGen among them
// (in the bundle because ShotDetector references it); interpreted, an assembly loads when code first needs it, so there "none at all" shows that nothing
// on this path touches FFmpeg.
Console.Error.WriteLine(atStart.Count == 0
    ? "No FFmpeg assembly loaded at all."
    : $"No FFmpeg assembly loaded by the runs (loaded before Main: {string.Join(", ", atStart)}).");
return 0;

static List<string> Ffmpeg() =>
    AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Where(n => n.StartsWith("FFmpeg", StringComparison.Ordinal)).Order().ToList();

// Node reads and writes the files (main.mjs); a file comes over as base64.
internal static partial class Host
{
    [JSImport("readFile", "main.mjs")]
    internal static partial string ReadFile(string path);

    [JSImport("writeFile", "main.mjs")]
    internal static partial void WriteFile(string path, string text);
}
