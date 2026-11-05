using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ManagedCodecs;

namespace ShotDetectorWeb;

public abstract partial class VideoInput
{
    /// <summary>
    /// Whether the frames come from the host's decoder (<see cref="IHostDecoder"/>, in the browser WebCodecs'
    /// <c>VideoDecoder</c>): a WebM's VP9 or AV1 (<see cref="WebmInput"/>), an MP4's (<see cref="Mp4Input"/>).
    /// </summary>
    public virtual bool DecodedByHost => false;

    /// <summary>
    /// The host decoder's frames for the packets <paramref name="read"/> gives from packet <paramref name="fromPacket"/> on
    /// (a keyframe; one shown frame a packet, VP9's and AV1's), each given that packet's pts in <see cref="Packets"/> and
    /// the picture type <paramref name="read"/> queued for it as it read the packet.
    /// </summary>
    private protected async IAsyncEnumerable<(VideoFrame Frame, PictureType Type)> HostFramesAsync(IHostDecoder decoder,
        HostDecoderConfig config, Func<ConcurrentQueue<PictureType>, IEnumerable<EncodedPacket>> read, int fromPacket,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A concurrent queue: a host's decoder may read the packets on a thread of its own (FfmpegDecoder writes them to
        // ffmpeg from a task) while the frames come out here. A plain Queue lost an Enqueue's count to a Dequeue on the
        // other thread now and then: the last frame then found the queue empty (type None), or a lost item shifted the
        // types after it by one.
        var types = new ConcurrentQueue<PictureType>();
        var pts = Packets.Pts.ToArray();
        int shown = fromPacket;
        await foreach (var frame in decoder.DecodeAsync(config, read(types), cancellationToken))
        {
            if (shown >= pts.Length)
            {
                frame.Dispose();
                throw new InvalidDataException("The decoder gave more frames than the video has packets.");
            }

            frame.Pts = pts[shown++];
            yield return (frame, types.TryDequeue(out var type) ? type : PictureType.None);
        }
    }

    /// <summary>
    /// WebCodecs' codec string for VP9 or AV1: VP9's 'vp09.PP.LL.DD' (level 4.1: Chromium decodes past it), AV1's from
    /// its 'av1C' (profile, level and tier) and bit depth.
    /// </summary>
    private protected static string HostCodecString(string codec, CodecHeader header, ReadOnlySpan<byte> av1C)
    {
        if (codec == "vp9")
        {
            int profile = (header.BitDepth > 8 ? 2 : 0) + (header.SubsampledY ? 0 : 1);
            return $"vp09.{profile:D2}.41.{header.BitDepth:D2}";
        }

        int seqProfile = av1C.Length > 1 ? av1C[1] >> 5 : 0, level = av1C.Length > 1 ? av1C[1] & 0x1F : 8;
        bool high = av1C.Length > 2 && (av1C[2] & 0x80) != 0;
        return $"av01.{seqProfile}.{level:D2}{(high ? 'H' : 'M')}.{header.BitDepth:D2}";
    }
}
