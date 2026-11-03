namespace ShotDetectorWeb;

/// <summary>
/// What a VP9 or AV1 stream says about its pictures in its own headers, which FFmpeg's decoders (vp9, libdav1d) give the
/// stream (ffprobe's pix_fmt, color_range, color_space, chroma_location): the format, bit depth and colour that
/// <see cref="WebmInput"/> describes the video with, the browser's <c>VideoFrame.colorSpace</c> not being the file's
/// (decision 2026-11-02-browser-decoder.md).
/// </summary>
public sealed record CodecHeader(int BitDepth, bool SubsampledX, bool SubsampledY, bool Monochrome, bool FullRange,
    int? Matrix, int? ChromaLocation)
{
    /// <summary>FFmpeg's pixel format for these pictures, or null for one ShotDetector's frame source doesn't take.</summary>
    public string? PixelFormat => (Monochrome, SubsampledX, SubsampledY, BitDepth) switch
    {
        (false, true, true, 8) => "yuv420p",
        (false, true, false, 8) => "yuv422p",
        (false, true, true, 10) => "yuv420p10le",
        _ => null,
    };

    /// <summary>The pictures' sampling and depth in a viewer's words, for a refusal.</summary>
    public string Describe() => Monochrome ? $"greyscale at {BitDepth} bits"
        : $"{(SubsampledX ? SubsampledY ? "4:2:0" : "4:2:2" : "4:4:4")} at {BitDepth} bits";
}

/// <summary>A VP9 key frame's uncompressed header (VP9 bitstream specification, 6.2): profile, depth, colour.</summary>
public static class Vp9Header
{
    // FFmpeg's vp9 decoder's colorspaces[] for VP9's color_space (CS_UNKNOWN, BT_601, BT_709, SMPTE_170, SMPTE_240,
    // BT_2020, reserved, RGB) as H.273 matrix coefficients: unspecified, bt470bg, bt709, smpte170m, smpte240m,
    // bt2020nc, reserved, gbr.
    static readonly int[] Matrices = [2, 5, 1, 6, 7, 9, 3, 0];

    /// <summary>
    /// The header of the key frame in <paramref name="block"/> (a WebM block: one frame, or a superframe whose frames
    /// are each read until a key frame is found); null when there is none.
    /// </summary>
    public static CodecHeader? Read(ReadOnlySpan<byte> block)
    {
        foreach (var (offset, length) in Frames(block))
        {
            if (Key(block.Slice(offset, length)) is { } header)
                return header;
        }

        return null;
    }

    /// <summary>Whether the frame in <paramref name="block"/> that is shown is a key frame or intra-only (FFmpeg's I).</summary>
    public static bool ShownIsIntra(ReadOnlySpan<byte> block)
    {
        var frames = Frames(block);
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            var (offset, length) = frames[i];
            var bits = new BitReader(block.Slice(offset, length));
            if (bits.Read(2) != 2)
                return false;
            int profile = bits.Read(1) | (bits.Read(1) << 1);
            if (profile == 3)
                bits.Read(1);
            if (bits.Read(1) == 1)
                continue; // show_existing_frame: a frame decoded earlier; FFmpeg's vp9 gives it the type it had
            bool key = bits.Read(1) == 0;
            bool show = bits.Read(1) == 1;
            bits.Read(1); // error_resilient_mode
            if (!show)
                continue;
            if (key)
                return true;
            return bits.Read(1) == 1; // intra_only
        }

        return false;
    }

    static CodecHeader? Key(ReadOnlySpan<byte> frame)
    {
        var bits = new BitReader(frame);
        if (bits.Read(2) != 2)
            return null;
        int profile = bits.Read(1) | (bits.Read(1) << 1);
        if (profile == 3)
            bits.Read(1);
        if (bits.Read(1) == 1 || bits.Read(1) != 0)
            return null; // a shown existing frame, or not a key frame
        bits.Read(2); // show_frame, error_resilient_mode
        if (bits.Read(8) != 0x49 || bits.Read(8) != 0x83 || bits.Read(8) != 0x42)
            return null;
        int depth = profile >= 2 ? bits.Read(1) == 1 ? 12 : 10 : 8;
        int colorSpace = bits.Read(3);
        bool fullRange, x = true, y = true;
        if (colorSpace != 7)
        {
            fullRange = bits.Read(1) == 1;
            if (profile is 1 or 3)
            {
                x = bits.Read(1) == 1;
                y = bits.Read(1) == 1;
            }
        }
        else
        {
            fullRange = true;
            x = y = false;
        }

        return new CodecHeader(depth, x, y, false, fullRange, Matrices[colorSpace], null);
    }

    /// <summary>The frames of a block: a superframe's by its index (Annex B), else the block whole.</summary>
    static List<(int Offset, int Length)> Frames(ReadOnlySpan<byte> block)
    {
        var frames = new List<(int, int)>();
        if (block.Length > 0)
        {
            byte marker = block[^1];
            if ((marker & 0xE0) == 0xC0)
            {
                int bytes = ((marker >> 3) & 3) + 1, count = (marker & 7) + 1, index = 2 + bytes * count;
                if (block.Length >= index && block[^index] == marker)
                {
                    int offset = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int size = 0;
                        for (int b = 0; b < bytes; b++)
                            size |= block[block.Length - index + 1 + i * bytes + b] << (8 * b);
                        if (offset + size > block.Length - index)
                            break;
                        frames.Add((offset, size));
                        offset += size;
                    }

                    return frames;
                }
            }

            frames.Add((0, block.Length));
        }

        return frames;
    }
}

/// <summary>An AV1 sequence header (AV1 specification, 5.5) from a Matroska track's CodecPrivate, an 'av1C' box's body.</summary>
public static class Av1Header
{
    /// <summary>
    /// The sequence header in <paramref name="av1C"/>'s configOBUs; null without one (then the stream's own, which isn't
    /// read here).
    /// </summary>
    public static CodecHeader? Read(ReadOnlySpan<byte> av1C)
    {
        if (av1C.Length < 4 || (av1C[0] & 0x80) == 0)
            return null;
        var obus = av1C[4..];
        while (obus.Length > 0)
        {
            int header = obus[0];
            int type = (header >> 3) & 0xF;
            bool extension = (header & 4) != 0, hasSize = (header & 2) != 0;
            int at = 1 + (extension ? 1 : 0);
            long size;
            if (hasSize)
            {
                size = Leb128(obus, ref at);
            }
            else
            {
                size = obus.Length - at;
            }

            if (at + size > obus.Length)
                return null;
            if (type == 1)
                return Sequence(obus.Slice(at, (int)size));
            obus = obus[(at + (int)size)..];
        }

        return null;
    }

    static long Leb128(ReadOnlySpan<byte> data, ref int at)
    {
        long value = 0;
        for (int i = 0; i < 8 && at < data.Length; i++)
        {
            byte b = data[at++];
            value |= (long)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
                break;
        }

        return value;
    }

    static CodecHeader Sequence(ReadOnlySpan<byte> obu)
    {
        var bits = new BitReader(obu);
        int profile = bits.Read(3);
        bits.Read(1); // still_picture
        bool reduced = bits.Read(1) == 1;
        bool decoderModelInfo = false;
        int bufferDelayLength = 0;
        if (reduced)
        {
            bits.Read(5); // seq_level_idx[0]
        }
        else
        {
            if (bits.Read(1) == 1) // timing_info_present_flag
            {
                bits.Read(32);
                bits.Read(32);
                if (bits.Read(1) == 1) // equal_picture_interval
                    bits.Uvlc();
                decoderModelInfo = bits.Read(1) == 1;
                if (decoderModelInfo)
                {
                    bufferDelayLength = bits.Read(5) + 1;
                    bits.Read(32);
                    bits.Read(5);
                    bits.Read(5);
                }
            }

            bool initialDisplayDelay = bits.Read(1) == 1;
            int operatingPoints = bits.Read(5) + 1;
            for (int i = 0; i < operatingPoints; i++)
            {
                bits.Read(12);
                int level = bits.Read(5);
                if (level > 7)
                    bits.Read(1);
                if (decoderModelInfo && bits.Read(1) == 1)
                {
                    bits.Read(bufferDelayLength);
                    bits.Read(bufferDelayLength);
                    bits.Read(1);
                }

                if (initialDisplayDelay && bits.Read(1) == 1)
                    bits.Read(4);
            }
        }

        int widthBits = bits.Read(4) + 1, heightBits = bits.Read(4) + 1;
        bits.Read(widthBits);
        bits.Read(heightBits);
        if (!reduced && bits.Read(1) == 1) // frame_id_numbers_present_flag
        {
            bits.Read(4);
            bits.Read(3);
        }

        bits.Read(3); // use_128x128_superblock, enable_filter_intra, enable_intra_edge_filter
        if (!reduced)
        {
            bits.Read(4); // interintra, masked compound, warped motion, dual filter
            bool orderHint = bits.Read(1) == 1;
            if (orderHint)
                bits.Read(2); // jnt_comp, ref_frame_mvs
            int forceScreenContentTools = bits.Read(1) == 1 ? 2 : bits.Read(1);
            if (forceScreenContentTools > 0 && bits.Read(1) == 0) // seq_choose_integer_mv
                bits.Read(1);
            if (orderHint)
                bits.Read(3);
        }

        bits.Read(3); // enable_superres, enable_cdef, enable_restoration

        // color_config
        bool high = bits.Read(1) == 1;
        int depth = profile == 2 && high ? bits.Read(1) == 1 ? 12 : 10 : high ? 10 : 8;
        bool mono = profile != 1 && bits.Read(1) == 1;
        int primaries = 2, transfer = 2, matrix = 2;
        if (bits.Read(1) == 1)
        {
            primaries = bits.Read(8);
            transfer = bits.Read(8);
            matrix = bits.Read(8);
        }

        bool fullRange, x, y;
        int? chroma = null;
        if (mono)
        {
            fullRange = bits.Read(1) == 1;
            x = y = true;
        }
        else if (primaries == 1 && transfer == 13 && matrix == 0)
        {
            fullRange = true;
            x = y = false;
        }
        else
        {
            fullRange = bits.Read(1) == 1;
            if (profile == 0)
            {
                x = y = true;
            }
            else if (profile == 1)
            {
                x = y = false;
            }
            else if (depth == 12)
            {
                x = bits.Read(1) == 1;
                y = x && bits.Read(1) == 1;
            }
            else
            {
                x = true;
                y = false;
            }

            if (x && y)
                chroma = bits.Read(2); // chroma_sample_position: 0 unknown, 1 vertical, 2 colocated
        }

        return new CodecHeader(depth, x, y, mono, fullRange, matrix, chroma);
    }
}

/// <summary>Reads bits most significant first.</summary>
ref struct BitReader(ReadOnlySpan<byte> data)
{
    readonly ReadOnlySpan<byte> _data = data;
    int _bit;

    public int Read(int count)
    {
        long value = 0;
        for (int i = 0; i < count; i++)
        {
            int at = _bit >> 3;
            int b = at < _data.Length ? (_data[at] >> (7 - (_bit & 7))) & 1 : 0;
            value = (value << 1) | (uint)b;
            _bit++;
        }

        return (int)value;
    }

    public void Uvlc()
    {
        int zeros = 0;
        while (zeros < 32 && Read(1) == 0)
            zeros++;
        if (zeros < 32)
            Read(zeros);
    }
}
