#if THREADS
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// The threaded bundle's exports (decision 2026-10-30-threads.md): the page's main thread (wwwroot/threads.js) runs a
/// one-worker run there, its decoder on several threads (<see cref="VideoInput.DecoderThreads"/>). On the main thread .NET
/// takes no synchronous export (it would block the page), so these are <see cref="Open"/>, <see cref="Stop"/>,
/// <see cref="Cancel"/> and the exports of the last run's result (<see cref="ScenesCsv"/>, <see cref="StatsCsv"/>,
/// <see cref="ShotsJson"/>, <see cref="ShotsHtml"/>, <see cref="Picture"/>, <see cref="Kept"/>) as tasks; <see cref="Detect"/>
/// and <see cref="Resume"/> are ones already. The file is opened and run on the thread pool (<see cref="Host"/>). A new export of the last run's result
/// needs one here too, and its name in threads.js's THREADED.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary><see cref="Open"/>, the frames then decoded on <paramref name="threads"/> threads.</summary>
    [JSExport]
    public static async Task<string> OpenThreaded(JSObject file, int threads)
    {
        _input = null;
        _result = null;
        _file?.Dispose();
        _file = file;
        var input = await Host.Run(() => Task.FromResult(DamagedPrefixed(() => VideoInput.Open(() => new BlobStream(file)))));
        input.DecoderThreads = threads;
        return Opened(input);
    }

    [JSExport]
    public static Task StopThreaded()
    {
        Stop();
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task CancelThreaded()
    {
        Cancel();
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task<string> ScenesCsvThreaded() => Task.FromResult(ScenesCsv());

    [JSExport]
    public static Task<string> ShotsJsonThreaded() => Task.FromResult(ShotsJson());

    [JSExport]
    public static Task<string> ShotsHtmlThreaded() => Task.FromResult(ShotsHtml());

    [JSExport]
    public static Task<string> PictureThreaded() => Task.FromResult(Picture());

    [JSExport]
    public static async Task<JSObject> KeptThreaded()
    {
        // As StatsCsvThreaded: BlobWriter calls JavaScript synchronously.
        await Task.Yield();
        return Kept();
    }

    [JSExport]
    public static async Task<JSObject> StatsCsvThreaded()
    {
        // Not inside the call from JavaScript: from there .NET calls no JavaScript synchronously (BlobWriter's parts).
        await Task.Yield();
        return StatsCsv();
    }
}
#endif
