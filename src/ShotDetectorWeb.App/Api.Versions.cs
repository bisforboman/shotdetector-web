using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShotDetectorWeb.App;

/// <summary>
/// The versions the page runs (backlog "The packages' versions on the page"): each package's, read from its assembly
/// in the bundle, so the footer says what Directory.Packages.props pinned, and the check (tools/app-check.mjs) that
/// the two agree.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The runtime and the packages as JSON (<see cref="VersionsResponse"/>): <c>{ runtime, packages: { "ShotDetector":
    /// "3.20.0", ... } }</c>, each package's version the NuGet one its assembly carries
    /// (<see cref="AssemblyInformationalVersionAttribute"/> without the commit after <c>+</c>; the assembly version
    /// without its fourth part if there is none), keyed by its package id, which is its assembly's name.
    /// </summary>
    [JSExport]
    public static string Versions() =>
        JsonSerializer.Serialize(VersionsResponse.Current(), VersionsJson.Default.VersionsResponse);
}

/// <summary>What <see cref="Api.Versions"/> answers: the runtime's description and the packages' versions by package id.</summary>
sealed record VersionsResponse(string Runtime, Dictionary<string, string> Packages)
{
    public static VersionsResponse Current()
    {
        // A type of each assembly: the assembly loads if it hasn't yet (interpreted, assemblies load on first use).
        Assembly[] assemblies =
        [
            typeof(ShotDetector.ShotDetection).Assembly,
            typeof(ShotDetector.FastYuv.SwscaleYuv420).Assembly,
            typeof(FrameReader.WaveformBuilder).Assembly,
            typeof(ManagedCodecs.VideoFrame).Assembly,
            typeof(ManagedCodecs.Containers.Mp4Demuxer).Assembly,
            typeof(ManagedCodecs.H264.H264Decoder).Assembly,
            typeof(ManagedCodecs.Aac.AacDecoder).Assembly,
        ];
        var packages = new Dictionary<string, string>();
        foreach (var assembly in assemblies)
            packages[assembly.GetName().Name ?? ""] = PackageVersion(assembly);
        return new(RuntimeInformation.FrameworkDescription, packages);
    }

    static string PackageVersion(Assembly assembly)
    {
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var v = assembly.GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VersionsResponse))]
partial class VersionsJson : JsonSerializerContext;
