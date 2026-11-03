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
            _freeze ?? PictureFreeze.KeptWithout), PictureJson.Default.PictureSummary);

    /// <summary>
    /// A range's bytes, little-endian: the scores' length, the scores, the picture measures' length, the measures, then the
    /// damaged frames the range's worker decoded (<see cref="VideoInput.DamagedFrames"/>: their count, then each, 0-based);
    /// empty when nothing was scored.
    /// </summary>
    static byte[] RangePacked(byte[] scores, PictureMeasures picture, IReadOnlyList<int> damaged)
    {
        if (scores.Length == 0)
            return [];
        var picked = picture.ToBytes();
        var bytes = new byte[4 + scores.Length + 4 + picked.Length + 4 * (1 + damaged.Count)];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, scores.Length);
        scores.CopyTo(span[4..]);
        int at = 4 + scores.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[at..], picked.Length);
        picked.CopyTo(span[(at + 4)..]);
        KeptRun.WriteFrames(span[(at + 4 + picked.Length)..], damaged);
        return bytes;
    }

    /// <summary><see cref="RangePacked"/>'s parts back.</summary>
    /// <exception cref="InvalidDataException">Not such bytes, or the picture's damaged.</exception>
    static (ReadOnlyMemory<byte> Scores, PictureMeasures Picture, int[] Damaged) RangeUnpacked(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        int length = span.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(span) : -1;
        if (length < 0 || length > span.Length - 8)
            throw new InvalidDataException("A range's bytes are damaged (the scores' length).");
        int at = 4 + length, picked = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        if (picked < 0 || picked > span.Length - at - 4)
            throw new InvalidDataException("A range's bytes are damaged (the picture's length).");
        return (bytes.Slice(4, length), PictureMeasures.FromBytes(span.Slice(at + 4, picked)),
            KeptRun.ReadFrames(span[(at + 4 + picked)..], "A range's bytes"));
    }
}
