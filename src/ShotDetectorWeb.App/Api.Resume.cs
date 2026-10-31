using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ShotDetector;

namespace ShotDetectorWeb.App;

/// <summary>
/// A stopped run resumed, in the same visit or a later one (backlog "Resume after Stop, across visits", decision
/// 2026-10-30-resume-across-visits.md): every run leaves its ranges' scores and picture measures (<see cref="Kept"/>),
/// which the page keeps in IndexedDB when it is stopped; <see cref="Resume"/> goes on from them on one worker, or the
/// page splits the rest (<see cref="Ranges"/> from <see cref="KeptRun"/>'s first frame) and <see cref="Merge"/> takes
/// them with the rest's ranges; <see cref="Merge"/> with them alone shows the stopped part again.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    static KeptRun? _kept;

    /// <summary>What a resume that fails on its kept scores says first: the page then detects from the start.</summary>
    const string NotResumed = "The stopped run didn't resume: ";

    /// <summary>
    /// <see cref="Detect"/> going on from a stopped run's <paramref name="kept"/> bytes (<see cref="Kept"/>'s), with the
    /// options it was scored with: the rest's shots told as they are known after the stopped part's, which aren't told
    /// again but for the last, cut short at the stop (ShotDetector tells it again from its number, to where it really
    /// ends); the result the whole run's. Kept bytes that don't go with the file and options (or damaged) throw with
    /// <see cref="NotResumed"/> in front of the reason.
    /// </summary>
    [JSExport]
    public static async Task<string> Resume(string optionsJson, [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] kept)
    {
        try
        {
            return await Continued(optionsJson, KeptRun.FromBytes(kept));
        }
        catch (Exception e) when (e is ShotDetectionException or InvalidDataException)
        {
            throw new InvalidOperationException(NotResumed + e.Message, e);
        }
    }

    /// <summary>
    /// The last run's ranges from the start and its picture measures as bytes (<see cref="KeptRun.ToBytes"/>) in a
    /// <c>Blob</c>, for the page to keep in IndexedDB and give back to <see cref="Resume"/> or <see cref="Merge"/>. Its
    /// first four bytes are the frame the rest would start at (a little-endian int: <see cref="VideoInput.Continue"/>'s
    /// range's first frame; -1 when none is left), which the page splits a long rest from.
    /// </summary>
    [JSExport]
    public static JSObject Kept()
    {
        var bytes = (_kept ?? throw new InvalidOperationException("Detect first."))
            .ToBytes(_input ?? throw new InvalidOperationException("Open a file first."));
        using var blob = new BlobWriter();
        blob.Write(bytes);
        return blob.ToBlob("application/octet-stream");
    }
}

/// <summary>
/// A run's ranges' scores from the start (<see cref="VideoInput.FromStart"/>) and its picture measures joined from frame 0
/// (null when it has none), as the page keeps them.
/// </summary>
sealed record KeptRun(IReadOnlyList<RangeScores> Scores, PictureMeasures? Picture)
{
    public static readonly KeptRun None = new([], null);

    /// <summary>
    /// Little-endian: the rest's first frame for <paramref name="input"/> (-1: none left), the ranges' count, each one's
    /// length, the picture's length (0: none), then each range's <see cref="RangeScores.ToBytes"/> and the picture's
    /// <see cref="PictureMeasures.ToBytes"/>, each checked by its own hash.
    /// </summary>
    public byte[] ToBytes(VideoInput input)
    {
        var scores = Scores.Select(s => s.ToBytes()).ToList();
        var picture = Picture?.ToBytes() ?? [];
        var bytes = new byte[4 * (3 + scores.Count) + scores.Sum(s => s.Length) + picture.Length];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, input.Continue(Scores).Rest?.FirstFrame ?? -1);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], scores.Count);
        int at = 8;
        foreach (var s in scores)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], s.Length);
            at += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(span[at..], picture.Length);
        at += 4;
        foreach (var s in scores.Append(picture))
        {
            s.CopyTo(span[at..]);
            at += s.Length;
        }

        return bytes;
    }

    /// <summary><see cref="ToBytes"/>' run back; empty bytes are <see cref="None"/>.</summary>
    /// <exception cref="InvalidDataException">Not such bytes.</exception>
    /// <exception cref="ShotDetectionException">A range's scores are damaged, or of another version.</exception>
    public static KeptRun FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return None;
        int Int(ReadOnlySpan<byte> b, int at) => at + 4 <= b.Length
            ? BinaryPrimitives.ReadInt32LittleEndian(b[at..])
            : throw new InvalidDataException("The kept run's bytes are damaged (cut short).");
        int count = Int(bytes, 4);
        if (count < 0 || count > bytes.Length / 4)
            throw new InvalidDataException("The kept run's bytes are damaged (the ranges' count).");
        var lengths = new int[count + 1];
        for (int i = 0; i <= count; i++)
            lengths[i] = Int(bytes, 8 + 4 * i);
        long total = 4L * (3 + count) + lengths.Sum(l => (long)l);
        if (lengths.Any(l => l < 0) || total != bytes.Length)
            throw new InvalidDataException("The kept run's bytes are damaged (the lengths).");
        int at = 4 * (3 + count);
        var scores = new List<RangeScores>();
        foreach (int length in lengths[..^1])
        {
            scores.Add(RangeScores.FromBytes(bytes.Slice(at, length)));
            at += length;
        }

        return new KeptRun(scores, lengths[^1] == 0 ? null : PictureMeasures.FromBytes(bytes[at..]));
    }
}
