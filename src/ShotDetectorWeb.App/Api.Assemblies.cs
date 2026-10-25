using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>For the checks (tools/app-check.mjs), not the page.</summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>The names of the assemblies loaded so far, sorted: the checks show nothing of FFmpeg's is loaded.</summary>
    [JSExport]
    public static string[] LoadedAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").Order(StringComparer.Ordinal).ToArray();
}
