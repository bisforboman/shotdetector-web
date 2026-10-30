using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// Where a run and the host's imports run (decision 2026-10-30-threads.md). In the page's worker (the bundle without
/// threads) everything is on the worker's one thread, and these call straight through. In the threaded bundle
/// (<c>-p:WasmEnableThreads=true</c>, <c>THREADS</c>), on the page's main thread, .NET runs the exports on its JavaScript
/// thread (the "deputy"), where only JavaScript may be called and nothing may block. So a run goes to the thread pool
/// (<see cref="Run{T}"/>), where the decoder's threads may be waited for and the file is read by waiting for each slice
/// (<see cref="Wait{T}"/>), and every call into JavaScript is handed back to the JavaScript thread (<see cref="Post"/>,
/// <see cref="Call"/>), in the order made.
/// </summary>
[SupportedOSPlatform("browser")]
static class Host
{
#if THREADS
    static SynchronizationContext? _js;

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool; called from an export, on the JavaScript thread, whose
    /// synchronization context the host's calls go back to.
    /// </summary>
    public static Task<T> Run<T>(Func<Task<T>> work)
    {
        _js = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Host.Run is called from an export, on .NET's JavaScript thread.");
        return Task.Run(work);
    }

    /// <summary>Calls <paramref name="call"/> on the JavaScript thread, after the calls posted before it, without waiting.</summary>
    public static void Post(Action call) => _js!.Post(static c => ((Action)c!)(), call);

    /// <summary>Calls <paramref name="call"/> on the JavaScript thread: a task done when its task is.</summary>
    public static Task Call(Func<Task> call) => Call(async () =>
    {
        await call();
        return true;
    });

    /// <summary><see cref="Call(Func{Task})"/> with a value.</summary>
    public static Task<T> Call<T>(Func<Task<T>> call)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _js!.Post(async _ =>
        {
            try
            {
                done.SetResult(await call());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }, null);
        return done.Task;
    }

    /// <summary>
    /// Calls <paramref name="call"/> on the JavaScript thread and waits for its value, on a thread of the pool (never
    /// the JavaScript thread, which must go on to answer it).
    /// </summary>
    public static T Wait<T>(Func<Task<T>> call) => Call(call).GetAwaiter().GetResult();
#else
    public static Task<T> Run<T>(Func<Task<T>> work) => work();

    public static void Post(Action call) => call();

    public static Task Call(Func<Task> call) => call();
#endif
}
