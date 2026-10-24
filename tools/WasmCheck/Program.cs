// ShotDetectorWeb.Core on WebAssembly: every run of tools/Shared/Runs.cs on each sample, its scene and stats CSVs written
// to the output folder under the names tools/NativeCheck gives its own (tools/check.sh compares the two folders). Fails
// if any assembly of FFmpeg's (FFmpeg.AutoGen) got loaded.
//   node main.mjs <out dir> <sample.mp4>...   (from the AppBundle directory; files are read and written through Node)
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using ShotDetectorWeb;
using ShotDetectorWeb.Checks;

string outDir = args[0];
Console.Error.WriteLine($"Vector128 accelerated: {Vector128.IsHardwareAccelerated}, processors: {Environment.ProcessorCount}, AOT: {!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");
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

var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Order().ToList();
Console.Error.WriteLine($"Loaded assemblies: {string.Join(", ", assemblies)}");
var ffmpeg = assemblies.Where(n => n.StartsWith("FFmpeg", StringComparison.Ordinal)).ToList();
if (ffmpeg.Count > 0)
{
    Console.Error.WriteLine($"FFmpeg assemblies loaded: {string.Join(", ", ffmpeg)}");
    return 1;
}

Console.Error.WriteLine("No FFmpeg assembly loaded.");
return 0;

// Node reads and writes the files (main.mjs); a file comes over as base64.
internal static partial class Host
{
    [JSImport("readFile", "main.mjs")]
    internal static partial string ReadFile(string path);

    [JSImport("writeFile", "main.mjs")]
    internal static partial void WriteFile(string path, string text);
}
