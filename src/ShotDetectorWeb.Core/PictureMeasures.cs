using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Intrinsics;
using ManagedCodecs;
using ShotDetector;

namespace ShotDetectorWeb;

/// <summary>
/// Measures each frame of a run as it is read (docs/decisions/2026-10-30-picture-measures.md): one pass over the frame
/// before ShotDetector converts it, about 2% of a run. What it keeps per frame is what FFmpeg's filters count, so that
/// <see cref="PictureMeasures"/> gives their figures exactly: <c>signalstats</c>' YMIN, YAVG, YMAX and BRNG,
/// <c>blackdetect</c>'s black pixels, <c>cropdetect</c>'s first and last lines over its limit, and whether the frame is
/// colour bars (by their definition: FFmpeg has no filter for them). With them it keeps the decoder's picture type
/// (I, P, B: FFmpeg's <c>pict_type</c>, ManagedCodecs' <see cref="VideoFrame.PictureType"/>), which costs nothing.
/// </summary>
public sealed class PictureMeter
{
    // blackdetect's pixel_black_th (0.10) and picture_black_ratio_th (0.98), cropdetect's limit (24/255), FFmpeg 8.1's
    // defaults.
    const double PixelBlack = 0.10;
    const int CropLimit = 24;

    readonly int _width, _height, _black;
    readonly bool _fullRange;
    readonly int _from, _first, _end;
    readonly BarColours _bars;
    int _index;

    /// <summary>A meter of the whole video's frames, from the first.</summary>
    public PictureMeter(YuvVideo video)
        : this(video, 0, 0, int.MaxValue)
    {
    }

    /// <summary>
    /// A meter of <paramref name="range"/>'s frames, as <see cref="VideoInput.RangeFramesAsync"/> gives them: from the
    /// frame before its first (not measured: the range before measures it) to the next range's first (not measured).
    /// </summary>
    public PictureMeter(YuvVideo video, FrameRange range)
        : this(video, Math.Max(range.FirstFrame - 1, 0), range.FirstFrame, range.EndFrame)
    {
    }

    PictureMeter(YuvVideo video, int from, int first, int end)
    {
        ArgumentNullException.ThrowIfNull(video);
        _width = video.Width;
        _height = video.Height;
        _fullRange = video.FullRange;
        // blackdetect's pixel_black_th_i, an unsigned int: 16 + 0.1 * 219 = 37.9 is 37, 0.1 * 255 = 25.5 is 25.
        _black = (int)(_fullRange ? PixelBlack * 255 : 16 + PixelBlack * (235 - 16));
        _bars = BarColours.For(_fullRange);
        _from = from;
        _first = first;
        _end = end;
        Measures = new PictureMeasures(_width, _height, _fullRange,
            video.TimeBase ?? throw new ArgumentException("The video has no time base.", nameof(video)), first);
    }

    /// <summary>The frames measured so far.</summary>
    public PictureMeasures Measures { get; }

    /// <summary>
    /// Measures <paramref name="frame"/>, the next frame given, if it is one of the meter's, and keeps its picture type,
    /// <paramref name="type"/>.
    /// </summary>
    public void Add(YuvFrame frame, PictureType type)
    {
        int index = _from + _index++;
        if (index < _first || index >= _end)
            return;
        var luma = Luma(frame.Y.Span, frame.YStride);
        int brng = luma.OutOfRange + ChromaOutOfRange(frame);
        var (top, bottom, left, right) = ContentLines(frame.Y.Span, frame.YStride);
        bool bars = IsBars(frame);
        Measures.Add(frame.Pts, luma.Min, luma.Max, luma.Sum, luma.Black, brng, top, bottom, left, right, bars, type);
    }

    /// <summary>The luma's sum, minimum and maximum, the pixels at or under blackdetect's threshold and those outside 16 to 235.</summary>
    (long Sum, byte Min, byte Max, int Black, int OutOfRange) Luma(ReadOnlySpan<byte> y, int stride)
    {
        var low = Vector128.Create((byte)255);
        var high = Vector128<byte>.Zero;
        var black = Vector128.Create((byte)_black);
        var under16 = Vector128.Create((byte)15);
        var over235 = Vector128.Create((byte)235);
        long sum = 0, blacks = 0, outside = 0;
        int min = 255, max = 0;
        for (int r = 0; r < _height; r++)
        {
            var row = y.Slice(r * stride, _width);
            var rowSum = Vector128<uint>.Zero;
            var rowBlack = Vector128<byte>.Zero;
            var rowOutside = Vector128<byte>.Zero;
            int x = 0, n = 0;
            for (; x + 16 <= _width; x += 16)
            {
                var v = Vector128.Create(row.Slice(x, 16));
                low = Vector128.Min(low, v);
                high = Vector128.Max(high, v);
                var (a, b) = Vector128.Widen(v);
                var (c, d) = Vector128.Widen(a + b);
                rowSum += c + d;
                // A compare is all ones where true: subtracting it counts, each byte lane up to 255 times.
                rowBlack -= Vector128.LessThanOrEqual(v, black);
                rowOutside -= Vector128.LessThanOrEqual(v, under16) | Vector128.GreaterThan(v, over235);
                if (++n == 255)
                {
                    blacks += Sum(rowBlack);
                    outside += Sum(rowOutside);
                    rowBlack = rowOutside = Vector128<byte>.Zero;
                    n = 0;
                }
            }

            blacks += Sum(rowBlack);
            outside += Sum(rowOutside);
            sum += Vector128.Sum(rowSum);
            for (; x < _width; x++)
            {
                int p = row[x];
                sum += p;
                min = Math.Min(min, p);
                max = Math.Max(max, p);
                if (p <= _black)
                    blacks++;
                if (p < 16 || p > 235)
                    outside++;
            }
        }

        if (_width >= 16)
        {
            for (int i = 0; i < 16; i++)
            {
                min = Math.Min(min, low.GetElement(i));
                max = Math.Max(max, high.GetElement(i));
            }
        }

        return (sum, (byte)min, (byte)max, (int)blacks, (int)outside);
    }

    static long Sum(Vector128<byte> counts)
    {
        var (a, b) = Vector128.Widen(counts);
        return Vector128.Sum(a + b);
    }

    /// <summary>
    /// signalstats' BRNG counts a luma pixel when it or its chroma sample is out of range (16 to 240): the luma pixels
    /// in range under each chroma sample out of range, which <see cref="Luma"/> didn't count.
    /// </summary>
    int ChromaOutOfRange(YuvFrame frame)
    {
        var u = frame.U.Span;
        var v = frame.V.Span;
        var y = frame.Y.Span;
        int width = (_width + 1) / 2, height = (_height + 1) / 2;
        var under16 = Vector128.Create((byte)15);
        var over240 = Vector128.Create((byte)240);
        int count = 0;
        for (int r = 0; r < height; r++)
        {
            var ur = u.Slice(r * frame.UStride, width);
            var vr = v.Slice(r * frame.VStride, width);
            int x = 0;
            for (; x + 16 <= width; x += 16)
            {
                var a = Vector128.Create(ur.Slice(x, 16));
                var b = Vector128.Create(vr.Slice(x, 16));
                var outside = Vector128.LessThanOrEqual(a, under16) | Vector128.GreaterThan(a, over240) |
                    Vector128.LessThanOrEqual(b, under16) | Vector128.GreaterThan(b, over240);
                if (outside == Vector128<byte>.Zero)
                    continue;
                for (int i = 0; i < 16; i++)
                {
                    if (outside.GetElement(i) != 0)
                        count += LumaInRange(y, frame.YStride, x + i, r);
                }
            }

            for (; x < width; x++)
            {
                if (ur[x] < 16 || ur[x] > 240 || vr[x] < 16 || vr[x] > 240)
                    count += LumaInRange(y, frame.YStride, x, r);
            }
        }

        return count;
    }

    /// <summary>The luma pixels of chroma sample (<paramref name="cx"/>, <paramref name="cy"/>) within 16 to 235.</summary>
    int LumaInRange(ReadOnlySpan<byte> y, int stride, int cx, int cy)
    {
        int n = 0;
        for (int r = 2 * cy; r < Math.Min(_height, 2 * cy + 2); r++)
        {
            for (int x = 2 * cx; x < Math.Min(_width, 2 * cx + 2); x++)
            {
                int p = y[r * stride + x];
                if (p is >= 16 and <= 235)
                    n++;
            }
        }

        return n;
    }

    /// <summary>
    /// cropdetect's lines: a line whose mean luma (an integer, rounded down) is over its limit has content. The first
    /// row with content above the last (cropdetect never looks at the last for it), the last row with content, the same
    /// for columns; -1 for none. <see cref="PictureMeasures"/> replays cropdetect's scan over them.
    /// </summary>
    (short Top, short Bottom, short Left, short Right) ContentLines(ReadOnlySpan<byte> y, int stride)
    {
        int top = -1, bottom = -1, left = -1, right = -1;
        for (int r = 0; r < _height - 1 && top < 0; r++)
        {
            if (RowSum(y.Slice(r * stride, _width)) / _width > CropLimit)
                top = r;
        }

        // Without content above the last row, only the last row can have it.
        for (int r = _height - 1; r >= Math.Max(top, top < 0 ? _height - 1 : 0) && bottom < 0; r--)
        {
            if (RowSum(y.Slice(r * stride, _width)) / _width > CropLimit)
                bottom = r;
        }

        Span<uint> sums = stackalloc uint[16];
        for (int c0 = 0; c0 < _width - 1 && left < 0; c0 += 16)
        {
            int n = ColumnSums(y, stride, c0, sums);
            for (int j = 0; j < n && c0 + j < _width - 1; j++)
            {
                if (sums[j] / (uint)_height > CropLimit)
                {
                    left = c0 + j;
                    break;
                }
            }
        }

        int lowest = left < 0 ? _width - 1 : left;
        for (int c1 = _width - 1; c1 >= lowest && right < 0; c1 -= 16)
        {
            int c0 = Math.Max(0, c1 - 15);
            ColumnSums(y, stride, c0, sums);
            for (int c = c1; c >= Math.Max(c0, lowest); c--)
            {
                if (sums[c - c0] / (uint)_height > CropLimit)
                {
                    right = c;
                    break;
                }
            }
        }

        return ((short)top, (short)bottom, (short)left, (short)right);
    }

    static long RowSum(ReadOnlySpan<byte> row)
    {
        var sum = Vector128<uint>.Zero;
        int x = 0;
        for (; x + 16 <= row.Length; x += 16)
        {
            var (a, b) = Vector128.Widen(Vector128.Create(row.Slice(x, 16)));
            var (c, d) = Vector128.Widen(a + b);
            sum += c + d;
        }

        long total = Vector128.Sum(sum);
        for (; x < row.Length; x++)
            total += row[x];
        return total;
    }

    /// <summary>The sums over every row of columns <paramref name="c0"/> on, sixteen or up to the last: how many.</summary>
    int ColumnSums(ReadOnlySpan<byte> y, int stride, int c0, Span<uint> sums)
    {
        int n = Math.Min(16, _width - c0);
        if (n == 16)
        {
            Vector128<uint> s0 = default, s1 = default, s2 = default, s3 = default;
            for (int r = 0; r < _height; r++)
            {
                var (a, b) = Vector128.Widen(Vector128.Create(y.Slice(r * stride + c0, 16)));
                var (a0, a1) = Vector128.Widen(a);
                var (b0, b1) = Vector128.Widen(b);
                s0 += a0;
                s1 += a1;
                s2 += b0;
                s3 += b1;
            }

            s0.CopyTo(sums);
            s1.CopyTo(sums[4..]);
            s2.CopyTo(sums[8..]);
            s3.CopyTo(sums[12..]);
            return 16;
        }

        sums.Clear();
        for (int r = 0; r < _height; r++)
        {
            for (int j = 0; j < n; j++)
                sums[j] += y[r * stride + c0 + j];
        }

        return n;
    }

    /// <summary>Colour bars, by their definition (<see cref="BarColours"/>): in the rows a quarter and half way down.</summary>
    bool IsBars(YuvFrame frame) =>
        _bars.InRow(frame, _width, _height / 4) && _bars.InRow(frame, _width, _height / 2);
}

/// <summary>
/// Colour bars by their definition, as FFmpeg draws them (<c>smptebars</c>, <c>smptehdbars</c>, <c>pal75bars</c>,
/// <c>pal100bars</c>): seven bars side by side, left to right white, yellow, cyan, green, magenta, red and blue, each
/// within <see cref="Tolerance"/> code values of its Y, U and V at 75% or 100%, in BT.601's or BT.709's matrix (one set
/// for all seven), each at least a twelfth of the width and none more than 1.5 times another, other columns between them
/// at most three chroma samples (a coded edge).
/// </summary>
sealed class BarColours
{
    const int Tolerance = 24;

    // Y, U, V in limited range (vsrc_testsrc.c's rainbow, rainbow100, rainbowhd; BT.709 at 100% from its matrix).
    static readonly byte[][,] Limited =
    [
        new byte[,] { { 180, 128, 128 }, { 162, 44, 142 }, { 131, 156, 44 }, { 112, 72, 58 }, { 84, 184, 198 }, { 65, 100, 212 }, { 35, 212, 114 } },
        new byte[,] { { 235, 128, 128 }, { 210, 16, 146 }, { 170, 166, 16 }, { 145, 54, 34 }, { 106, 202, 222 }, { 81, 90, 240 }, { 41, 240, 110 } },
        new byte[,] { { 180, 128, 128 }, { 168, 44, 136 }, { 145, 147, 44 }, { 133, 63, 52 }, { 63, 193, 204 }, { 51, 109, 212 }, { 28, 212, 120 } },
        new byte[,] { { 235, 128, 128 }, { 219, 16, 138 }, { 188, 154, 16 }, { 173, 42, 26 }, { 78, 214, 230 }, { 63, 102, 240 }, { 32, 240, 118 } },
    ];

    static readonly BarColours LimitedRange = new(Limited);
    static readonly BarColours FullRange = new(Limited.Select(ToFull).ToArray());

    readonly byte[][,] _sets;

    BarColours(byte[][,] sets) => _sets = sets;

    public static BarColours For(bool fullRange) => fullRange ? FullRange : LimitedRange;

    static byte[,] ToFull(byte[,] set)
    {
        var full = new byte[7, 3];
        for (int i = 0; i < 7; i++)
        {
            full[i, 0] = (byte)Math.Clamp(Math.Round((set[i, 0] - 16) * 255 / 219.0), 0, 255);
            for (int c = 1; c < 3; c++)
                full[i, c] = (byte)Math.Clamp(Math.Round((set[i, c] - 128) * 255 / 224.0 + 128), 0, 255);
        }

        return full;
    }

    /// <summary>Whether luma row <paramref name="row"/> (and its chroma row) is the seven bars in one of the sets.</summary>
    public bool InRow(YuvFrame frame, int width, int row)
    {
        int columns = (width + 1) / 2;
        var y = frame.Y.Span.Slice(row * frame.YStride);
        var u = frame.U.Span.Slice((row >> 1) * frame.UStride, columns);
        var v = frame.V.Span.Slice((row >> 1) * frame.VStride, columns);
        Span<sbyte> labels = columns <= 4096 ? stackalloc sbyte[columns] : new sbyte[columns];
        foreach (var set in _sets)
        {
            for (int c = 0; c < columns; c++)
            {
                int luma = 2 * c + 1 < width ? (y[2 * c] + y[2 * c + 1] + 1) >> 1 : y[2 * c];
                labels[c] = Nearest(set, luma, u[c], v[c]);
            }

            if (Sequence(labels, columns))
                return true;
        }

        return false;
    }

    static sbyte Nearest(byte[,] set, int y, int u, int v)
    {
        for (int i = 0; i < 7; i++)
        {
            if (Math.Abs(y - set[i, 0]) <= Tolerance && Math.Abs(u - set[i, 1]) <= Tolerance && Math.Abs(v - set[i, 2]) <= Tolerance)
                return (sbyte)i;
        }

        return -1;
    }

    /// <summary>Runs of labels 0 to 6 in order, each wide enough, with at most three other columns between them.</summary>
    static bool Sequence(ReadOnlySpan<sbyte> labels, int columns)
    {
        int least = Math.Max(2, columns / 12);
        for (int start = 0; start < columns; start++)
        {
            if (labels[start] != 0 || (start > 0 && labels[start - 1] == 0))
                continue;
            int at = start, narrowest = int.MaxValue, widest = 0;
            bool found = true;
            for (int bar = 0; bar < 7 && found; bar++)
            {
                int gap = 0;
                while (at < columns && labels[at] != bar && gap <= 2)
                {
                    at++;
                    gap++;
                }

                int begin = at;
                while (at < columns && labels[at] == bar)
                    at++;
                int run = at - begin;
                found = run >= least && (bar > 0 || gap == 0);
                narrowest = Math.Min(narrowest, run);
                widest = Math.Max(widest, run);
            }

            if (found && widest * 2 <= narrowest * 3)
                return true;
        }

        return false;
    }
}

/// <summary>
/// The picture's measures of a run's frames (<see cref="PictureMeter"/>), by column, from <see cref="FirstFrame"/>, and
/// FFmpeg's figures from them: <c>signalstats</c>' (<see cref="YAvg"/>, <see cref="Brng"/>), <c>blackdetect</c>'s
/// (<see cref="BlackSpans"/>), <c>cropdetect</c>'s (<see cref="Crop"/>, <see cref="FrameCrop"/>), and the colour bars
/// (<see cref="BarsSpans"/>); and each frame's picture type (<see cref="Type"/>, <see cref="Types"/>). The ranges of a split run join (<see cref="Join"/>) into the whole run's, byte for byte.
/// </summary>
public sealed class PictureMeasures
{
    const uint Magic = 0x32434950; // "PIC2"

    readonly List<long> _pts = [];
    readonly List<byte> _min = [], _max = [];
    readonly List<long> _sum = [];
    readonly List<int> _black = [], _brng = [];
    readonly List<short> _top = [], _bottom = [], _left = [], _right = [];
    readonly List<bool> _bars = [];
    readonly List<PictureType> _type = [];

    internal PictureMeasures(int width, int height, bool fullRange, FrameReader.Rational timeBase, int firstFrame)
    {
        Width = width;
        Height = height;
        FullRange = fullRange;
        TimeBase = timeBase;
        FirstFrame = firstFrame;
    }

    public int Width { get; }

    public int Height { get; }

    public bool FullRange { get; }

    /// <summary>The frames' pts' time base (<see cref="YuvVideo.TimeBase"/>).</summary>
    public FrameReader.Rational TimeBase { get; }

    /// <summary>The first frame's index in the video (display order).</summary>
    public int FirstFrame { get; }

    /// <summary>How many frames, from <see cref="FirstFrame"/>.</summary>
    public int Count => _pts.Count;

    int Pixels => Width * Height;

    internal void Add(long pts, byte min, byte max, long sum, int black, int brng, short top, short bottom, short left,
        short right, bool bars, PictureType type)
    {
        _pts.Add(pts);
        _min.Add(min);
        _max.Add(max);
        _sum.Add(sum);
        _black.Add(black);
        _brng.Add(brng);
        _top.Add(top);
        _bottom.Add(bottom);
        _left.Add(left);
        _right.Add(right);
        _bars.Add(bars);
        _type.Add(type);
    }

    public long Pts(int i) => _pts[i];

    /// <summary>signalstats' YMIN.</summary>
    public int YMin(int i) => _min[i];

    /// <summary>signalstats' YMAX.</summary>
    public int YMax(int i) => _max[i];

    /// <summary>signalstats' YAVG: the luma's mean.</summary>
    public double YAvg(int i) => 1.0 * _sum[i] / Pixels;

    /// <summary>signalstats' BRNG: the share of pixels out of broadcast range, luma 16 to 235 or chroma 16 to 240.</summary>
    public double Brng(int i) => 1.0 * _brng[i] / Pixels;

    /// <summary>The pixels BRNG counts.</summary>
    public int BrngPixels(int i) => _brng[i];

    /// <summary>blackdetect's: at least 98% of the pixels at or under its threshold.</summary>
    public bool IsBlack(int i) => (double)_black[i] / Pixels >= 0.98;

    /// <summary>Colour bars (<see cref="PictureMeter"/>, by their definition).</summary>
    public bool IsBars(int i) => _bars[i];

    /// <summary>The frame's picture type as the decoder says it (FFmpeg's <c>pict_type</c>).</summary>
    public PictureType Type(int i) => _type[i];

    /// <summary>
    /// Every frame's picture type, one character each, as FFmpeg's <c>av_get_picture_type_char</c> writes it (ffprobe's
    /// <c>pict_type</c>): I, P, B, S, i (SI), p (SP), b (BI), ? (none).
    /// </summary>
    public string Types() => string.Create(Count, _type, static (chars, types) =>
    {
        for (int i = 0; i < chars.Length; i++)
            chars[i] = TypeChar(types[i]);
    });

    /// <summary>FFmpeg's <c>av_get_picture_type_char</c>.</summary>
    public static char TypeChar(PictureType type) => type switch
    {
        PictureType.I => 'I',
        PictureType.P => 'P',
        PictureType.B => 'B',
        PictureType.S => 'S',
        PictureType.SI => 'i',
        PictureType.SP => 'p',
        PictureType.BI => 'b',
        _ => '?',
    };

    /// <summary>
    /// blackdetect's spans with <c>d=<paramref name="minSeconds"/></c>: from a black frame's pts to the first frame
    /// after that isn't black, or at the end the last frame's pts (FFmpeg's, a FIXME there), kept when that lasts at
    /// least <paramref name="minSeconds"/> (in the time base, rounded down, as blackdetect).
    /// </summary>
    public IReadOnlyList<FrameSpan> BlackSpans(double minSeconds = 2)
    {
        long least = (long)(minSeconds / (TimeBase.Num / (double)TimeBase.Den));
        return Spans(IsBlack, endAtLast: true).Where(s => s.EndPts - s.StartPts >= least).ToArray();
    }

    /// <summary>The colour bars' spans: from a bars frame to the first frame after that isn't (the end at the file's end).</summary>
    public IReadOnlyList<FrameSpan> BarsSpans() => Spans(IsBars, endAtLast: false);

    List<FrameSpan> Spans(Func<int, bool> on, bool endAtLast)
    {
        var spans = new List<FrameSpan>();
        int start = -1;
        for (int i = 0; i < Count; i++)
        {
            if (on(i) && start < 0)
            {
                start = i;
            }
            else if (!on(i) && start >= 0)
            {
                spans.Add(new FrameSpan(FirstFrame + start, FirstFrame + i, _pts[start], _pts[i]));
                start = -1;
            }
        }

        if (start >= 0)
        {
            int last = Count - 1;
            spans.Add(endAtLast
                ? new FrameSpan(FirstFrame + start, FirstFrame + last, _pts[start], _pts[last])
                : new FrameSpan(FirstFrame + start, FirstFrame + Count, _pts[start], _pts[last]));
        }

        return spans;
    }

    /// <summary>cropdetect's figures for frame <paramref name="i"/> alone (<c>reset=1:skip=0</c>).</summary>
    public CropArea FrameCrop(int i)
    {
        var state = CropState.Start(Width, Height);
        state.Scan(_top[i], _bottom[i], _left[i], _right[i]);
        return state.Area();
    }

    /// <summary>
    /// cropdetect's figures with its defaults (<c>limit=24/255:round=16:skip=2:reset=0</c>) after the last frame: the
    /// area holding every frame's content but the first two; null for three frames or fewer, or for a range not from frame 0.
    /// </summary>
    public CropArea? Crop()
    {
        const int skip = 2;
        if (FirstFrame != 0 || Count <= skip)
            return null;
        var state = CropState.Start(Width, Height);
        for (int i = skip; i < Count; i++)
            state.Scan(_top[i], _bottom[i], _left[i], _right[i]);
        return state.Area();
    }

    /// <summary>The first <paramref name="count"/> frames' measures (a run's, cut to the frames its result read).</summary>
    public PictureMeasures Take(int count)
    {
        var taken = new PictureMeasures(Width, Height, FullRange, TimeBase, FirstFrame);
        for (int i = 0; i < Math.Min(count, Count); i++)
            taken.Add(_pts[i], _min[i], _max[i], _sum[i], _black[i], _brng[i], _top[i], _bottom[i], _left[i], _right[i], _bars[i], _type[i]);
        return taken;
    }

    /// <summary>
    /// Ranges' measures joined from the first frame on, while each starts where the one before ends (a range stopped
    /// short ends them, as <see cref="VideoInput.FromStart"/> ends the scores); null when none starts at frame 0.
    /// </summary>
    public static PictureMeasures? Join(IEnumerable<PictureMeasures> ranges)
    {
        PictureMeasures? joined = null;
        foreach (var range in ranges.OrderBy(r => r.FirstFrame))
        {
            if (joined is null)
            {
                if (range.FirstFrame != 0)
                    return null;
                joined = range.Take(range.Count);
                continue;
            }

            if (range.FirstFrame != joined.Count || (range.Width, range.Height, range.FullRange) != (joined.Width, joined.Height, joined.FullRange))
                break;
            for (int i = 0; i < range.Count; i++)
            {
                joined.Add(range._pts[i], range._min[i], range._max[i], range._sum[i], range._black[i], range._brng[i],
                    range._top[i], range._bottom[i], range._left[i], range._right[i], range._bars[i], range._type[i]);
            }
        }

        return joined;
    }

    /// <summary>The measures as bytes, for a range's worker to hand to the one that joins them, checked by a hash.</summary>
    public byte[] ToBytes()
    {
        const int header = 4 * 4 + 1 + 4 * 2 + 4;
        const int perFrame = 8 + 1 + 1 + 8 + 4 + 4 + 2 * 4 + 1 + 1;
        var bytes = new byte[header + perFrame * Count + 8];
        var w = new Writer(bytes);
        w.UInt(Magic);
        w.Int(Width);
        w.Int(Height);
        w.Int(FirstFrame);
        w.Byte(FullRange ? (byte)1 : (byte)0);
        w.Int(TimeBase.Num);
        w.Int(TimeBase.Den);
        w.Int(Count);
        for (int i = 0; i < Count; i++)
        {
            w.Long(_pts[i]);
            w.Byte(_min[i]);
            w.Byte(_max[i]);
            w.Long(_sum[i]);
            w.Int(_black[i]);
            w.Int(_brng[i]);
            w.Short(_top[i]);
            w.Short(_bottom[i]);
            w.Short(_left[i]);
            w.Short(_right[i]);
            w.Byte(_bars[i] ? (byte)1 : (byte)0);
            w.Byte((byte)_type[i]);
        }

        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(w.At), Hash(bytes.AsSpan(0, w.At)));
        return bytes;
    }

    /// <summary><see cref="ToBytes"/>' measures back.</summary>
    /// <exception cref="InvalidDataException">Not such bytes, or damaged.</exception>
    public static PictureMeasures FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || Hash(bytes[..^8]) != BinaryPrimitives.ReadUInt64LittleEndian(bytes[^8..]))
            throw new InvalidDataException("The picture's measures are damaged (their hash differs).");
        var r = new Reader(bytes[..^8]);
        if (r.UInt() != Magic)
            throw new InvalidDataException("These aren't the picture's measures.");
        int width = r.Int(), height = r.Int(), first = r.Int();
        bool full = r.Byte() != 0;
        var timeBase = new FrameReader.Rational(r.Int(), r.Int());
        var measures = new PictureMeasures(width, height, full, timeBase, first);
        int count = r.Int();
        for (int i = 0; i < count; i++)
        {
            measures.Add(r.Long(), r.Byte(), r.Byte(), r.Long(), r.Int(), r.Int(), r.Short(), r.Short(), r.Short(), r.Short(),
                r.Byte() != 0, (PictureType)r.Byte());
        }

        return measures;
    }

    // FNV-1a, 64 bits: enough to tell damaged bytes from a worker (no package for it).
    static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in bytes)
            hash = (hash ^ b) * 1099511628211;
        return hash;
    }

    ref struct Writer(Span<byte> bytes)
    {
        readonly Span<byte> _bytes = bytes;

        public int At { get; private set; }

        public void Byte(byte v) => _bytes[At++] = v;

        public void Short(short v) => Put(v, 2, BinaryPrimitives.WriteInt16LittleEndian);

        public void Int(int v) => Put(v, 4, BinaryPrimitives.WriteInt32LittleEndian);

        public void UInt(uint v) => Put(v, 4, BinaryPrimitives.WriteUInt32LittleEndian);

        public void Long(long v) => Put(v, 8, BinaryPrimitives.WriteInt64LittleEndian);

        void Put<T>(T v, int size, SpanWrite<T> write)
        {
            write(_bytes.Slice(At, size), v);
            At += size;
        }
    }

    delegate void SpanWrite<T>(Span<byte> span, T value);

    ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        readonly ReadOnlySpan<byte> _bytes = bytes;
        int _at;

        public byte Byte() => Take(1)[0];

        public short Short() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

        public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        ReadOnlySpan<byte> Take(int n)
        {
            if (_at + n > _bytes.Length)
                throw new InvalidDataException("The picture's measures end early.");
            var span = _bytes.Slice(_at, n);
            _at += n;
            return span;
        }
    }

    /// <summary>cropdetect's state (x1, x2, y1, y2) and its scan of a frame, replayed from the frame's content lines.</summary>
    struct CropState
    {
        int _x1, _x2, _y1, _y2;

        public static CropState Start(int width, int height) =>
            new() { _x1 = width - 1, _y1 = height - 1, _x2 = 0, _y2 = 0 };

        // vf_cropdetect.c's FIND, in order: y1 from the top while under y1, y2 from the bottom while over max(y2, y1),
        // then x1 and x2 the same way.
        public void Scan(int top, int bottom, int left, int right)
        {
            if (top >= 0 && top < _y1)
                _y1 = top;
            if (bottom > Math.Max(_y2, _y1))
                _y2 = bottom;
            if (left >= 0 && left < _x1)
                _x1 = left;
            if (right > Math.Max(_x2, _x1))
                _x2 = right;
        }

        // Rounded as cropdetect does (round=16): x and y up to even, w and h down to a multiple of 16, centred.
        public readonly CropArea Area()
        {
            const int round = 16;
            int x = (_x1 + 1) & ~1, y = (_y1 + 1) & ~1;
            int w = _x2 - x + 1, h = _y2 - y + 1;
            int shrink = w % round;
            w -= shrink;
            x += (shrink / 2 + 1) & ~1;
            shrink = h % round;
            h -= shrink;
            y += (shrink / 2 + 1) & ~1;
            return new CropArea(_x1, _x2, _y1, _y2, w, h, x, y);
        }
    }
}

/// <summary>Frames <see cref="FirstFrame"/> up to <see cref="EndFrame"/> and their pts, as FFmpeg reports the span.</summary>
public sealed record FrameSpan(int FirstFrame, int EndFrame, long StartPts, long EndPts);

/// <summary>cropdetect's figures: the content's first and last columns and rows, and the crop (<c>crop=w:h:x:y</c>).</summary>
public sealed record CropArea(int X1, int X2, int Y1, int Y2, int W, int H, int X, int Y)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"crop={W}:{H}:{X}:{Y}");
}
