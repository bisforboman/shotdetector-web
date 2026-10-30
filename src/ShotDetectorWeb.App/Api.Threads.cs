#if THREADS
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// The threaded bundle's exports (decision 2026-10-30-threads.md): the page's main thread (wwwroot/threads.js) runs a
/// one-worker run there, its decoder on several threads (<see cref="Mp4Input.DecoderThreads"/>). On the main thread .NET
/// takes no synchronous export (it would block the page), so these are <see cref="Open"/>, <see cref="Stop"/>,
/// <see cref="Cancel"/>, <see cref="ScenesCsv"/> and <see cref="StatsCsv"/> as tasks; <see cref="Detect"/> is one
/// already. The file is opened and run on the thread pool (<see cref="Host"/>).
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
        var input = await Host.Run(() => Task.FromResult(DamagedPrefixed(() => Mp4Input.Open(() => new BlobStream(file)))));
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
    public static async Task<JSObject> StatsCsvThreaded()
    {
        // Not inside the call from JavaScript: from there .NET calls no JavaScript synchronously (BlobWriter's parts).
        await Task.Yield();
        return StatsCsv();
    }
}
#endif
