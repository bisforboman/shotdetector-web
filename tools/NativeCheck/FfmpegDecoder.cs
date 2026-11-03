using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ManagedCodecs;
using ShotDetectorWeb;

namespace ShotDetectorWeb.Checks;

/// <summary>
/// FFmpeg standing in for the browser's decoder (decision 2026-11-02-webm-browser-decoder.md): the packets
/// <see cref="WebmInput"/> gives, written as an IVF stream to the pinned ffmpeg, which decodes them with the decoder
/// <c>Detect(path)</c> uses (vp9, libdav1d) and writes each frame packed in the video's format, as the browser's
/// <c>VideoFrame.copyTo</c> does. The description, the timing and the conversion are then held to <c>Detect(path)</c>
/// natively, as for any sample; page-check holds Chromium's decoder to the same files.
/// </summary>
sealed class FfmpegDecoder(string libs) : IHostDecoder
{
    /// <summary>Opens <paramref name="path"/> as the page opens a file: a WebM's frames from this decoder.</summary>
    public static VideoInput Open(string path) =>
        VideoInput.Open(() => File.OpenRead(path), new FfmpegDecoder(Environment.GetEnvironmentVariable("SHOTDETECTOR_FFMPEG_LIBS")
            ?? throw new InvalidOperationException("Set SHOTDETECTOR_FFMPEG_LIBS.")));

    public async IAsyncEnumerable<VideoFrame> DecodeAsync(HostDecoderConfig config, IEnumerable<EncodedPacket> packets,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string pixelFormat = config.Format switch
        {
            PixelFormat.Yuv420P => "yuv420p",
            PixelFormat.Yuv422P => "yuv422p",
            PixelFormat.Yuv420P10 => "yuv420p10le",
            _ => throw new NotSupportedException(config.Format.ToString()),
        };
        string ffmpeg = Path.Combine(libs, "ffmpeg");
        if (!File.Exists(ffmpeg))
            ffmpeg = "ffmpeg";
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "ivf", "-i", "pipe:0", "-map", "0:v:0",
            "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "pipe:1" })
            start.ArgumentList.Add(arg);
        start.Environment["LD_LIBRARY_PATH"] = libs;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg didn't start.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var writing = Task.Run(() => Write(process.StandardInput.BaseStream, config, packets), cancellationToken);

        int bytes = config.Format == PixelFormat.Yuv420P10 ? 2 : 1;
        int chromaWidth = (config.Width + 1) / 2;
        int chromaHeight = config.Format == PixelFormat.Yuv422P ? config.Height : (config.Height + 1) / 2;
        int[] strides = [config.Width * bytes, chromaWidth * bytes, chromaWidth * bytes];
        int luma = strides[0] * config.Height, chroma = strides[1] * chromaHeight;
        var output = process.StandardOutput.BaseStream;
        try
        {
            while (true)
            {
                var buffer = new byte[luma + 2 * chroma];
                int read = await output.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
                if (read == 0)
                    break;
                if (read < buffer.Length)
                    throw new InvalidDataException("ffmpeg's last frame is cut short.");
                yield return new VideoFrame(config.Width, config.Height, config.Format, [buffer, buffer, buffer], strides,
                    [0, luma, luma + chroma]);
            }

            await writing;
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"ffmpeg failed: {await errors}");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    // An IVF stream (VP9's or AV1's): a 32-byte header, then each packet with its size and pts.
    static void Write(Stream stdin, HostDecoderConfig config, IEnumerable<EncodedPacket> packets)
    {
        try
        {
            Span<byte> header = stackalloc byte[32];
            "DKIF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 32);
            (config.FfmpegCodec == "av1" ? "AV01"u8 : "VP90"u8).CopyTo(header[8..]);
            BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)config.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)config.Height);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 1000);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], 1);
            stdin.Write(header);
            Span<byte> frame = stackalloc byte[12];
            foreach (var packet in packets)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)packet.Data.Length);
                BinaryPrimitives.WriteInt64LittleEndian(frame[4..], packet.Pts);
                stdin.Write(frame);
                stdin.Write(packet.Data);
            }
        }
        catch (IOException)
        {
            // ffmpeg ended first (the frames stopped early): what it gave is what was asked for.
        }
        finally
        {
            try
            {
                stdin.Close();
            }
            catch (IOException)
            {
            }
        }
    }
}
