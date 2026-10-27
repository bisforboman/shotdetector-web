using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManagedCodecs;
using ManagedCodecs.H264;

namespace ShotDetectorWeb.WebCodecsWasm;

/// <summary>
/// ManagedCodecs' H.264 decoder, one access unit a call, as Mp4Input decodes for the page (one thread, frames copied out
/// of the decoder's pictures and given back to its pool once done with). Each call returns how many frames came out.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Decoding
{
    static H264Decoder? decoder;
    static long sequence;

    /// <summary>A new decoder for a stream whose avcC is <paramref name="avcC"/>.</summary>
    [JSExport]
    public static void Start(byte[] avcC)
    {
        decoder = new H264Decoder(avcC);
        sequence = 0;
    }

    /// <summary>One access unit (the MP4 sample's length-prefixed NAL units); a discarded one is decoded and gives no frame.</summary>
    [JSExport]
    public static int Decode(byte[] data, double pts, bool keyframe, bool discard)
    {
        long t = (long)pts;
        return Done(Decoder.Decode(new Packet(0, sequence++, data, keyframe, t, t, 0, Discard: discard)));
    }

    /// <summary>The end of the stream: the frames still held back.</summary>
    [JSExport]
    public static int Flush() => Done(Decoder.Flush());

    static H264Decoder Decoder => decoder ?? throw new InvalidOperationException("Start first.");

    static int Done(IReadOnlyList<VideoFrame> frames)
    {
        foreach (var frame in frames)
            frame.Dispose();
        return frames.Count;
    }
}
