using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManagedCodecs;

namespace ShotDetectorWeb.App;

/// <summary>
/// The browser's own decoder for what ManagedCodecs doesn't decode (decision 2026-11-02-webm-browser-decoder.md): WebM's
/// VP9 and AV1 through WebCodecs' <c>VideoDecoder</c> in the worker (wwwroot/video-decode.js), the packets given one at a
/// time and each frame awaited, copied out of the browser's <c>VideoFrame</c> packed in the video's format (I420,
/// I420P10, I422; NV12 de-interleaved) and into .NET's memory. A browser without the codec, or whose frames aren't Y, U
/// and V planes (Firefox's BGRX), refuses the run in the page's words.
/// </summary>
[SupportedOSPlatform("browser")]
sealed partial class JsDecoder : IHostDecoder
{
    public static readonly JsDecoder Instance = new();

    public async IAsyncEnumerable<VideoFrame> DecodeAsync(HostDecoderConfig config, IEnumerable<EncodedPacket> packets,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string format = config.Format switch
        {
            PixelFormat.Yuv420P => "I420",
            PixelFormat.Yuv422P => "I422",
            PixelFormat.Yuv420P10 => "I420P10",
            _ => throw new NotSupportedException(config.Format.ToString()),
        };
        string refused = await DecoderOpen(config.Codec, config.Width, config.Height, format);
        if (refused.Length > 0)
            throw new UnsupportedVideoException(
                $"This browser can't decode the video ({config.FfmpegCodec.ToUpperInvariant()}, FFmpeg's {config.FfmpegCodec}) " +
                $"for the page: {refused} Chromium's decoder (Chrome, Edge) reads it; {Refusal.Scope}");

        int bytes = config.Format == PixelFormat.Yuv420P10 ? 2 : 1;
        int chromaWidth = (config.Width + 1) / 2;
        int chromaHeight = config.Format == PixelFormat.Yuv422P ? config.Height : (config.Height + 1) / 2;
        int[] strides = [config.Width * bytes, chromaWidth * bytes, chromaWidth * bytes];
        int luma = strides[0] * config.Height, chroma = strides[1] * chromaHeight;
        int[] offsets = [0, luma, luma + chroma];
        try
        {
            foreach (var packet in packets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DecoderDecode(packet.Data, packet.Pts, packet.Keyframe);
                while (await DecoderNext(false) == 1)
                    yield return Take(config, luma + 2 * chroma, strides, offsets);
            }

            while (await DecoderNext(true) == 1)
                yield return Take(config, luma + 2 * chroma, strides, offsets);
        }
        finally
        {
            DecoderClose();
        }
    }

    static VideoFrame Take(HostDecoderConfig config, int size, int[] strides, int[] offsets)
    {
        var buffer = new byte[size];
        DecoderTake(buffer);
        return new VideoFrame(config.Width, config.Height, config.Format, [buffer, buffer, buffer], strides, offsets);
    }

    /// <summary>Configures a decoder for the codec (WebCodecs' string): "" when it can decode it, else why not.</summary>
    [JSImport("decoderOpen", "host")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> DecoderOpen(string codec, int width, int height, string format);

    /// <summary>Gives the decoder one packet (copied out of .NET's memory during the call).</summary>
    [JSImport("decoderDecode", "host")]
    private static partial void DecoderDecode([JSMarshalAs<JSType.MemoryView>] Span<byte> data, double pts, bool keyframe);

    /// <summary>
    /// 1 when a frame is ready to take; 0 when the decoder wants the next packet; -1 once flushed (end) and every frame
    /// taken. Rejects with the decoder's error, or the frames' format when it isn't Y, U and V planes.
    /// </summary>
    [JSImport("decoderNext", "host")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<int> DecoderNext(bool end);

    /// <summary>Copies the next frame's packed planes into <paramref name="destination"/>.</summary>
    [JSImport("decoderTake", "host")]
    private static partial void DecoderTake([JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

    /// <summary>Closes the decoder and drops any frame not taken.</summary>
    [JSImport("decoderClose", "host")]
    private static partial void DecoderClose();
}
