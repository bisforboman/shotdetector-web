using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShotDetectorWeb.App;

/// <summary>
/// The video's packets (backlog "Bitrate and keyframes"): what <see cref="VideoInput.Packets"/> kept while
/// <see cref="Open"/> walked the file, for the bitrate over time and the encoder's keyframes, without decoding.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    /// <summary>
    /// The opened file's video packets as JSON (<see cref="PacketsResponse"/>), by column: in decode order, as
    /// <c>ffprobe -show_packets -select_streams v:0</c> lists them.
    /// </summary>
    [JSExport]
    public static string Packets()
    {
        var input = _input ?? throw new InvalidOperationException("Open a file first.");
        return JsonSerializer.Serialize(PacketsResponse.From(input.Packets), PacketsJson.Default.PacketsResponse);
    }
}

/// <summary>
/// <see cref="VideoPackets"/> for the page, by column: packet <c>i</c>'s pts (<see cref="Pts"/>, in
/// <see cref="TimeBase"/>: its time in seconds is pts × num / den, ffprobe's pts_time) and size in bytes
/// (<see cref="Sizes"/>); the keyframes' and the discarded packets' indexes (ffprobe's K and D: an edit list's packets,
/// decoded but not shown), in order, rather than a flag per packet; and where each packet's data starts in the file
/// (<see cref="Offsets"/>, <see cref="VideoPackets.Offsets"/>), for an AVI, whose MJPEG packets are each a JPEG the page
/// cuts from the picked file and the browser shows (backlog "The picture of an AVI on the page"); null for an MP4.
/// </summary>
sealed record PacketsResponse(FrameReader.Rational TimeBase, long[] Pts, int[] Sizes, int[] Keyframes, int[] Discarded,
    long[]? Offsets)
{
    public static PacketsResponse From(VideoPackets p)
    {
        var keyframes = new List<int>();
        var discarded = new List<int>();
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Flags[i].HasFlag(PacketFlags.Keyframe))
                keyframes.Add(i);
            if (p.Flags[i].HasFlag(PacketFlags.Discard))
                discarded.Add(i);
        }

        return new(p.TimeBase, p.Pts.ToArray(), p.Sizes.ToArray(), [.. keyframes], [.. discarded],
            p.Offsets.IsEmpty ? null : p.Offsets.ToArray());
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PacketsResponse))]
partial class PacketsJson : JsonSerializerContext;
