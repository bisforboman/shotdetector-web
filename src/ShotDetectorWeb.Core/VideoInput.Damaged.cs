using System.Collections.Concurrent;
using System.Globalization;

namespace ShotDetectorWeb;

/// <summary>
/// The damaged frames (decision 2026-11-02-damaged-jpeg.md, the owner's A): a JPEG whose scan runs past its data decodes
/// with what the scan didn't reach 0 (ManagedCodecs 0.6.0, <see cref="ManagedCodecs.VideoFrame.IsDamaged"/>), where FFmpeg
/// leaves a frame from its buffer pool, which one depending on its threads. The frame goes to ShotDetector as decoded;
/// the run's warnings name it (<see cref="DamagedWarning"/>), since the stats around it can't equal FFmpeg's.
/// </summary>
public abstract partial class VideoInput
{
    // The damaged frames' pts, as any run on this input decoded them: a property of the file, so never cleared.
    readonly ConcurrentDictionary<long, byte> _damaged = new();

    /// <summary>Notes <paramref name="frame"/> when the decoder says it is damaged.</summary>
    private protected void NoteDamaged(ManagedCodecs.VideoFrame frame)
    {
        if (frame.IsDamaged)
            _damaged.TryAdd(frame.Pts, 0);
    }

    /// <summary>
    /// The damaged frames among the first <paramref name="frames"/> (0-based, in presentation order) that the runs on this
    /// input have decoded so far, in order. A range's or a resumed run's are its own: <see cref="DamagedWarning"/> takes
    /// them together.
    /// </summary>
    public IReadOnlyList<int> DamagedFrames(int frames)
    {
        if (_damaged.IsEmpty)
            return [];
        var shown = new List<long>(Packets.Count);
        for (int i = 0; i < Packets.Count; i++)
        {
            if (!Packets.Flags[i].HasFlag(PacketFlags.Discard))
                shown.Add(Packets.Pts[i]);
        }

        shown.Sort();
        var found = new SortedSet<int>();
        foreach (long pts in _damaged.Keys)
        {
            int frame = shown.BinarySearch(pts);
            if (frame >= 0 && frame < frames)
                found.Add(frame);
        }

        return [.. found];
    }

    /// <summary>
    /// The run's warning for its damaged frames (0-based, <see cref="DamagedFrames"/>; any order, repeats ignored) of the
    /// first <paramref name="frames"/> it read, numbered from 1 as the stats CSV numbers them; null when none.
    /// </summary>
    public static string? DamagedWarning(IEnumerable<int> damaged, int frames)
    {
        var numbers = damaged.Where(f => f >= 0 && f < frames).Distinct().Order().Select(f => (f + 1).ToString(CultureInfo.InvariantCulture)).ToList();
        if (numbers.Count == 0)
            return null;
        string which = numbers.Count == 1 ? $"Frame {numbers[0]} is" : $"Frames {string.Join(", ", numbers)} are";
        return $"{which} damaged (a JPEG that ends before its picture does): the part it doesn't reach is decoded black, " +
            "where FFmpeg leaves an earlier frame's picture, which one depending on its threads, so the stats around " +
            (numbers.Count == 1 ? "it" : "them") + " can't equal FFmpeg's.";
    }
}
