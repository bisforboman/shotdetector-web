using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using FrameReader;

namespace ShotDetectorWeb.App;

/// <summary>
/// The picture's measures (backlog "Picture measures", decision 2026-10-30-picture-measures.md): every run measures each
/// frame as it reads it (<see cref="PictureMeter"/>), a split run each range in its worker, its measures going out with
/// the range's scores (<see cref="RangeBytes"/>) and joined by <see cref="Merge"/>; <see cref="Picture"/> gives the last
/// run's.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    static PictureMeasures? _picture;

    // The last run's still pictures, or why it didn't look for them (decision 2026-11-01-freeze-spans.md).
    static PictureFreeze? _freeze;

    /// <summary>
    /// The last run's picture measures as JSON (<see cref="PictureSummary"/>, camelCase), for the frames it read, with its
    /// still pictures: a run that read every frame in order from the first looked for them, a split or resumed run says
    /// why it didn't.
    /// </summary>
    [JSExport]
    public static string Picture() =>
        JsonSerializer.Serialize(PictureSummary.Of(_picture ?? throw new InvalidOperationException("Detect first."),
            _freeze ?? PictureFreeze.Resumed), PictureJson.Default.PictureSummary);

    /// <summary>A range's bytes: the scores' length, the scores, then the picture's measures; empty when nothing was scored.</summary>
    static byte[] RangePacked(byte[] scores, PictureMeasures picture)
    {
        if (scores.Length == 0)
            return [];
        var picked = picture.ToBytes();
        var bytes = new byte[4 + scores.Length + picked.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, scores.Length);
        scores.CopyTo(bytes, 4);
        picked.CopyTo(bytes, 4 + scores.Length);
        return bytes;
    }

    /// <summary><see cref="RangePacked"/>'s parts back.</summary>
    /// <exception cref="InvalidDataException">Not such bytes, or the picture's damaged.</exception>
    static (ReadOnlyMemory<byte> Scores, PictureMeasures Picture) RangeUnpacked(ReadOnlyMemory<byte> bytes)
    {
        int length = bytes.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.Span) : -1;
        if (length < 0 || length > bytes.Length - 4)
            throw new InvalidDataException("A range's bytes are damaged (the scores' length).");
        return (bytes.Slice(4, length), PictureMeasures.FromBytes(bytes.Span[(4 + length)..]));
    }
}
