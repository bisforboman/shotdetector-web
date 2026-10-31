namespace ShotDetectorWeb;

/// <summary>
/// The fields of an H.264 sequence parameter set (ITU-T H.264, 7.3.2.1.1 and E.1.1) that decide the decoded frames'
/// format and colour tags as FFmpeg's decoder reports them. ManagedCodecs' own parser is internal and skips the colour
/// fields of the VUI, so this reads them.
/// </summary>
/// <param name="ChromaLocation">
/// FFmpeg's chroma location (AVChromaLocation: 0 unspecified, 1 left, 2 center, 3 topleft, ...): the VUI's
/// chroma_sample_loc_type_top_field plus one where it has one (0 for a type over 5), else left, the H.264 default
/// (libavcodec's h2645_vui.c, and its SPS parser without a VUI).
/// </param>
internal sealed record SpsInfo(int ChromaFormatIdc, int BitDepthLuma, int BitDepthChroma, bool VideoSignalTypePresent, bool FullRange, int? MatrixCoefficients,
    int ChromaLocation = SpsInfo.Left)
{
    /// <summary>AVCHROMA_LOC_LEFT.</summary>
    public const int Left = 1;

    /// <summary>The first SPS of an 'avcC' record (ISO/IEC 14496-15, 5.3.3.1).</summary>
    public static SpsInfo FromAvcConfig(ReadOnlySpan<byte> avcC)
    {
        if (avcC.Length < 8 || (avcC[5] & 0x1F) == 0)
            throw new UnsupportedVideoException("The H.264 track's configuration ('avcC') has no sequence parameter set.");
        int length = (avcC[6] << 8) | avcC[7];
        if (8 + length > avcC.Length || length < 2)
            throw new UnsupportedVideoException("The H.264 track's sequence parameter set is truncated.");
        return Parse(avcC.Slice(8, length));
    }

    /// <summary>Parses a NAL unit holding an SPS (with its one-byte NAL header).</summary>
    public static SpsInfo Parse(ReadOnlySpan<byte> nal)
    {
        // Emulation prevention: 00 00 03 -> 00 00.
        var rbsp = new List<byte>(nal.Length);
        int zeros = 0;
        for (int i = 1; i < nal.Length; i++)
        {
            if (zeros >= 2 && nal[i] == 3)
            {
                zeros = 0;
                continue;
            }

            zeros = nal[i] == 0 ? zeros + 1 : 0;
            rbsp.Add(nal[i]);
        }

        var r = new Bits(rbsp.ToArray());
        int profile = r.U(8);
        r.U(16); // constraint flags, level_idc
        r.Ue(); // seq_parameter_set_id
        int chroma = 1, depthLuma = 8, depthChroma = 8;
        if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
        {
            chroma = r.Ue();
            if (chroma == 3)
                r.U(1); // separate_colour_plane_flag
            depthLuma = r.Ue() + 8;
            depthChroma = r.Ue() + 8;
            r.U(1); // qpprime_y_zero_transform_bypass_flag
            if (r.U(1) == 1)
            {
                for (int i = 0; i < (chroma != 3 ? 8 : 12); i++)
                {
                    if (r.U(1) == 1)
                        SkipScalingList(r, i < 6 ? 16 : 64);
                }
            }
        }

        r.Ue(); // log2_max_frame_num_minus4
        int pocType = r.Ue();
        if (pocType == 0)
        {
            r.Ue(); // log2_max_pic_order_cnt_lsb_minus4
        }
        else if (pocType == 1)
        {
            r.U(1); // delta_pic_order_always_zero_flag
            r.Se(); // offset_for_non_ref_pic
            r.Se(); // offset_for_top_to_bottom_field
            int cycle = r.Ue();
            for (int i = 0; i < cycle; i++)
                r.Se();
        }

        r.Ue(); // max_num_ref_frames
        r.U(1); // gaps_in_frame_num_value_allowed_flag
        r.Ue(); // pic_width_in_mbs_minus1
        r.Ue(); // pic_height_in_map_units_minus1
        if (r.U(1) == 0) // frame_mbs_only_flag
            r.U(1); // mb_adaptive_frame_field_flag
        r.U(1); // direct_8x8_inference_flag
        if (r.U(1) == 1)
        {
            r.Ue();
            r.Ue();
            r.Ue();
            r.Ue();
        }

        bool signalType = false, fullRange = false;
        int? matrix = null;
        int location = Left;
        if (r.U(1) == 1)
        {
            if (r.U(1) == 1 && r.U(8) == 255)
                r.U(32); // Extended_SAR: sar_width, sar_height
            if (r.U(1) == 1)
                r.U(1); // overscan_appropriate_flag
            if (r.U(1) == 1)
            {
                signalType = true;
                r.U(3); // video_format
                fullRange = r.U(1) == 1;
                if (r.U(1) == 1)
                {
                    r.U(16); // colour_primaries, transfer_characteristics
                    matrix = r.U(8);
                }
            }

            if (r.U(1) == 1) // chroma_loc_info_present_flag
            {
                int top = r.Ue(); // chroma_sample_loc_type_top_field
                r.Ue(); // chroma_sample_loc_type_bottom_field
                location = top <= 5 ? top + 1 : 0;
            }
        }

        return new SpsInfo(chroma, depthLuma, depthChroma, signalType, fullRange, matrix, location);
    }

    static void SkipScalingList(Bits r, int size)
    {
        int last = 8, next = 8;
        for (int j = 0; j < size; j++)
        {
            if (next != 0)
                next = (last + r.Se() + 256) % 256;
            last = next == 0 ? last : next;
        }
    }

    sealed class Bits(byte[] data)
    {
        int _pos;

        public int U(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
            {
                if (_pos >= data.Length * 8)
                    throw new UnsupportedVideoException("The H.264 sequence parameter set is truncated.");
                v = (v << 1) | ((data[_pos >> 3] >> (7 - (_pos & 7))) & 1);
                _pos++;
            }

            return v;
        }

        public int Ue()
        {
            int zeros = 0;
            while (U(1) == 0)
            {
                if (++zeros > 31)
                    throw new UnsupportedVideoException("The H.264 sequence parameter set is malformed.");
            }

            return (int)((1L << zeros) - 1 + (uint)U(zeros));
        }

        public int Se()
        {
            int k = Ue();
            return (k & 1) == 1 ? (k + 1) / 2 : -(k / 2);
        }
    }
}
