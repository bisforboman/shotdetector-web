// The reference side of the WebCodecs comparison (README, "WebCodecs"; tools/webcodecs/ is the browser's side).
//   WebCodecsCheck prepare --out dir sample.mp4...
//     For each sample, into dir: <name>.units, the video's access units in decode order exactly as ManagedCodecs'
//     Mp4Demuxer gives them (each: int32 size, int64 pts, a byte of flags: 1 keyframe, 2 discard; then the data;
//     little-endian), <name>.hashes, one line per frame Mp4Input decodes (the frames ShotDetector gets), in display
//     order: "pts y u v", each plane's visible samples packed row after row, the first 16 bytes of their SHA-256 in hex,
//     and <name>.json, what the browser needs to configure its decoder and what the frames are.
//   WebCodecsCheck diff sample.mp4 frame planes.yuv
//     Where frame number `frame` (display order, from 0) of the sample differs from planes.yuv (that frame's Y, U and V
//     planes packed, as the browser copied them out): per plane, how many samples differ, the first one's position and
//     values, and the largest difference; as JSON on stdout.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ManagedCodecs.Containers.Mp4;
using ShotDetector;
using ShotDetectorWeb;

if (args is ["prepare", "--out", var outDir, .. var samples] && samples.Length > 0)
{
    Directory.CreateDirectory(outDir);
    foreach (string sample in samples)
        Prepare(sample, outDir);
    return 0;
}

if (args is ["diff", var file, var frameText, var planesFile])
{
    Console.WriteLine(Diff(file, int.Parse(frameText), File.ReadAllBytes(planesFile)));
    return 0;
}

Console.Error.WriteLine("Usage: WebCodecsCheck prepare --out dir sample.mp4... | WebCodecsCheck diff sample.mp4 frame planes.yuv");
return 2;

static void Prepare(string sample, string outDir)
{
    string name = Path.GetFileNameWithoutExtension(sample);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var input = Mp4Input.Open(sample);
    var video = input.Video;

    // The access units, from a demuxer of their own: the packets Mp4Input's frames are decoded from.
    using var stream = File.OpenRead(sample);
    using var demuxer = new Mp4Demuxer(stream);
    var info = demuxer.Video!;
    int packets = 0, discarded = 0;
    using (var units = new BufferedStream(File.Create(Path.Combine(outDir, name + ".units")), 1 << 20))
    {
        Span<byte> head = stackalloc byte[13];
        foreach (var packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex != info.StreamIndex)
                continue;
            BinaryPrimitives.WriteInt32LittleEndian(head, packet.Data.Length);
            BinaryPrimitives.WriteInt64LittleEndian(head[4..], packet.Pts);
            head[12] = (byte)((packet.IsKeyframe ? 1 : 0) | (packet.Discard ? 2 : 0));
            units.Write(head);
            units.Write(packet.Data.Span);
            packets++;
            if (packet.Discard)
                discarded++;
        }
    }

    int frames = 0;
    using (var hashes = new StreamWriter(Path.Combine(outDir, name + ".hashes")))
    {
        foreach (var frame in input.Frames())
        {
            hashes.WriteLine($"{frame.Pts} {Hash(frame.Y.Span, frame.YStride, video.Width, video.Height)} " +
                $"{Hash(frame.U.Span, frame.UStride, (video.Width + 1) / 2, (video.Height + 1) / 2)} " +
                $"{Hash(frame.V.Span, frame.VStride, (video.Width + 1) / 2, (video.Height + 1) / 2)}");
            frames++;
        }
    }

    // avcC's bytes 1 to 3 are the profile, its constraint flags and the level: the codec string RFC 6381 gives them.
    var avcC = info.CodecPrivate.Span;
    var meta = new Meta(
        File: Path.GetFullPath(sample),
        Bytes: new FileInfo(sample).Length,
        Codec: $"avc1.{avcC[1]:x2}{avcC[2]:x2}{avcC[3]:x2}",
        Description: Convert.ToBase64String(avcC),
        Width: video.Width,
        Height: video.Height,
        TimeBase: info.TimeBase.ToString(),
        FrameRate: $"{video.FrameRate.Num}/{video.FrameRate.Den}",
        PixelFormat: video.PixelFormat,
        ColorSpace: video.ColorSpace,
        Packets: packets,
        Discarded: discarded,
        Frames: frames);
    File.WriteAllText(Path.Combine(outDir, name + ".json"), JsonSerializer.Serialize(meta, Json.Default.Meta));
    Console.WriteLine($"{name}: {packets} access units ({discarded} discarded), {frames} frames, {video.Width}x{video.Height}, {watch.Elapsed.TotalSeconds:F1} s");
}

// The first 16 bytes of the SHA-256 of a plane's visible samples, packed (what VideoFrame.copyTo gives with a tight layout).
static string Hash(ReadOnlySpan<byte> plane, int stride, int width, int height)
{
    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    for (int y = 0; y < height; y++)
        sha.AppendData(plane.Slice(y * stride, width));
    return Convert.ToHexStringLower(sha.GetHashAndReset(), 0, 16);
}

static string Diff(string sample, int index, byte[] theirs)
{
    var input = Mp4Input.Open(sample);
    int w = input.Video.Width, h = input.Video.Height, cw = (w + 1) / 2, ch = (h + 1) / 2;
    if (theirs.Length != w * h + 2 * cw * ch)
        throw new InvalidDataException($"{theirs.Length} bytes, not the {w * h + 2 * cw * ch} of a {w}x{h} frame's planes.");
    var frame = input.Frames().ElementAt(index);
    var planes = new List<PlaneDiff>();
    int offset = 0;
    foreach (var (plane, stride, pw, ph, label) in new[] { (frame.Y, frame.YStride, w, h, "Y"), (frame.U, frame.UStride, cw, ch, "U"), (frame.V, frame.VStride, cw, ch, "V") })
    {
        var mine = plane.Span;
        int differing = 0, largest = 0;
        PlaneDiff? first = null;
        for (int y = 0; y < ph; y++)
        {
            for (int x = 0; x < pw; x++)
            {
                int a = mine[y * stride + x], b = theirs[offset + y * pw + x];
                if (a == b)
                    continue;
                differing++;
                largest = Math.Max(largest, Math.Abs(a - b));
                first ??= new PlaneDiff(label, 0, 0, x, y, a, b);
            }
        }

        planes.Add((first ?? new PlaneDiff(label, 0, 0, -1, -1, 0, 0)) with { Differing = differing, Largest = largest });
        offset += pw * ph;
    }

    return JsonSerializer.Serialize(new FrameDiff(index, frame.Pts, planes), Json.Default.FrameDiff);
}

record Meta(string File, long Bytes, string Codec, string Description, int Width, int Height, string TimeBase, string FrameRate,
    string PixelFormat, string ColorSpace, int Packets, int Discarded, int Frames);

/// <summary>One plane of one frame: how many samples differ, the largest difference, and the first one (x, y; -1 for none), ManagedCodecs' value and the browser's.</summary>
record PlaneDiff(string Plane, int Differing, int Largest, int X, int Y, int ManagedCodecs, int WebCodecs);

record FrameDiff(int Frame, long Pts, List<PlaneDiff> Planes);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Meta))]
[System.Text.Json.Serialization.JsonSerializable(typeof(FrameDiff))]
partial class Json : System.Text.Json.Serialization.JsonSerializerContext;
