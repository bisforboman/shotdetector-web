using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ShotDetectorWeb.App;

/// <summary>
/// The frame types (backlog "Frame types (I, P, B) on the strip"): every run keeps each frame's picture type with the
/// picture's measures (<see cref="PictureMeter"/>; a split run each range in its worker, joined by <see cref="Merge"/>),
/// as ManagedCodecs' decoder says it, FFmpeg's <c>pict_type</c>; <see cref="FrameTypes"/> gives the last run's.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The last run's frames' types, one character each in display order, as ffprobe's <c>pict_type</c> prints them
    /// (<see cref="PictureMeasures.Types"/>: I, P, B; S, i, p, b and ? for the others), for the frames it read.
    /// </summary>
    [JSExport]
    public static string FrameTypes() => (_picture ?? throw new InvalidOperationException("Detect first.")).Types();
}
