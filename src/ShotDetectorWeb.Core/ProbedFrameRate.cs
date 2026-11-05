namespace ShotDetectorWeb;

/// <summary>
/// FFmpeg's frame rates for a video stream whose container gives none (a WebM without a DefaultDuration, decision
/// 2026-11-05-webm-without-default-duration.md): FFmpeg 8.1's <c>avformat_find_stream_info</c> over the packets it
/// probes, ported. Its <c>ff_rfps_add_frame</c> keeps, for each standard rate, the error of the packets' timestamps
/// against that rate's grid; <c>ff_rfps_calculate</c> makes <c>r_frame_rate</c> of them, and <c>avg_frame_rate</c> of
/// <c>r_frame_rate</c> when the mean probed duration is within a tick of it. For packets without durations (Matroska's
/// SimpleBlocks without a DefaultDuration), in a time base FFmpeg thinks unreliable (one finer than 1/100 s).
/// </summary>
public static class ProbedFrameRate
{
    /// <summary>The rates FFmpeg says: <c>r_frame_rate</c> and <c>avg_frame_rate</c> (null: 0/0, none).</summary>
    public readonly record struct Rates(Fraction RFrameRate, Fraction? AverageFrameRate);

    /// <summary>A rate as FFmpeg reduces it.</summary>
    public readonly record struct Fraction(long Num, long Den);

    /// <summary>A packet in file order: the video stream's, with its timestamp (its dts: WebM's pts, no B-frames), or
    /// another stream's; and its size, which the 5 MB probe counts for every stream.</summary>
    public readonly record struct ProbedPacket(bool Video, long Timestamp, int Size);

    const int MaxStdTimebases = 30 * 12 + 30 + 3 + 6;
    const long ProbeSize = 5_000_000, MaxAnalyzeMicroseconds = 5_000_000;

    /// <summary>
    /// FFmpeg's rates for the video stream of <paramref name="packets"/> (in file order), its time base
    /// <paramref name="timeBaseNum"/>/<paramref name="timeBaseDen"/>.
    /// </summary>
    public static Rates Estimate(IEnumerable<ProbedPacket> packets, long timeBaseNum, long timeBaseDen)
    {
        double tb = timeBaseNum / (double)timeBaseDen;
        // avformat_find_stream_info: 20 frames' durations, twice as many in a time base coarser than 0.5 ms.
        int framesToAnalyze = tb > 0.0005 ? 40 : 20;
        var errors = new double[2, 2, MaxStdTimebases];
        long? lastDts = null, fpsFirst = null, fpsLast = null;
        int fpsFirstIdx = 0, fpsLastIdx = 0, durationCount = 0, nbFrames = 0;
        long durationSum = 0, durationGcd = 0, readSize = 0;

        foreach (var packet in packets)
        {
            // The loop's checks before each read: all the video's info found, or 5 MB read.
            if (durationCount >= framesToAnalyze || readSize >= ProbeSize)
                break;
            readSize += packet.Size;
            if (!packet.Video)
                continue;
            long dts = packet.Timestamp;
            if (nbFrames > 1)
            {
                if (fpsLast is long l && l >= dts)
                    fpsFirst = fpsLast = null;
                if (fpsLast is long last && fpsFirst is long first && fpsLastIdx > fpsFirstIdx &&
                    (ulong)(dts - last) / 1000 > (ulong)(last - first) / (ulong)(fpsLastIdx - fpsFirstIdx))
                    fpsFirst = fpsLast = null;
                if (fpsFirst is null)
                {
                    fpsFirst = dts;
                    fpsFirstIdx = nbFrames;
                }

                fpsLast = dts;
                fpsLastIdx = nbFrames;
                // max_analyze_duration (5 s): without packet durations or an average rate, from the frames' dts once
                // over 30 are read.
                if (nbFrames > 30 && fpsFirst is long f0 && fpsLast is long f1 &&
                    Rescale(f1 - f0, timeBaseNum, timeBaseDen) >= MaxAnalyzeMicroseconds)
                    break;
            }

            // ff_rfps_add_frame.
            if (lastDts is long previous && dts > previous)
            {
                double seconds = dts * tb;
                long duration = dts - previous;
                for (int i = 0; i < MaxStdTimebases; i++)
                {
                    if (errors[0, 1, i] >= 1e10)
                        continue;
                    double sdts = seconds * StdFramerate(i) / (1001 * 12);
                    for (int j = 0; j < 2; j++)
                    {
                        long ticks = (long)Math.Round(sdts + j * 0.5, MidpointRounding.ToEven);
                        double error = sdts - ticks + j * 0.5;
                        errors[j, 0, i] += error;
                        errors[j, 1, i] += error * error;
                    }
                }

                durationCount++;
                durationSum += duration;
                if (durationCount % 10 == 0)
                {
                    int n = durationCount;
                    for (int i = 0; i < MaxStdTimebases; i++)
                    {
                        if (errors[0, 1, i] >= 1e10)
                            continue;
                        double a0 = errors[0, 0, i] / n, error0 = errors[0, 1, i] / n - a0 * a0;
                        double a1 = errors[1, 0, i] / n, error1 = errors[1, 1, i] / n - a1 * a1;
                        if (error0 > 0.04 && error1 > 0.04)
                            errors[0, 1, i] = errors[1, 1, i] = 2e10;
                    }
                }

                // The first 4 values ignored: they might have some random jitter.
                if (durationCount > 3)
                    durationGcd = Gcd(durationGcd, duration);
            }

            lastDts = dts;
            nbFrames++;
        }

        // ff_rfps_calculate, its time base unreliable.
        Fraction? r = null;
        if (durationCount > 15 && durationGcd > Math.Max(1, timeBaseDen / (500 * timeBaseNum)))
            r = Reduce(timeBaseDen, timeBaseNum * durationGcd, int.MaxValue);
        if (r is null && durationCount > 1)
        {
            int num = 0;
            double bestError = 0.01;
            double meanSeconds = tb * durationSum / durationCount;
            for (int j = 0; j < MaxStdTimebases; j++)
            {
                // No packet durations: the rates under 12 fps left out; and those whose frame is over 0.8 of the mean's.
                if (StdFramerate(j) < 1001 * 12)
                    continue;
                if (meanSeconds < 1001 * 12.0 * 0.8 / StdFramerate(j))
                    continue;
                for (int k = 0; k < 2; k++)
                {
                    int n = durationCount;
                    double a = errors[k, 0, j] / n, error = errors[k, 1, j] / n - a * a;
                    if (error < bestError && bestError > 0.000000001)
                    {
                        bestError = error;
                        num = StdFramerate(j);
                    }
                }
            }

            // Not more than 1 % over the time base's own rate (1/tb) to match a standard rate.
            if (num != 0 && num / (12.0 * 1001) < 1.01 * (timeBaseDen / (double)timeBaseNum))
                r = Reduce(num, 12 * 1001, int.MaxValue);
        }

        Fraction? average = null;
        if (r is { } rate && durationSum > 0 && durationCount > 2 &&
            Math.Abs(1.0 / (rate.Num / (double)rate.Den * tb) - durationSum / (double)durationCount) <= 1.0)
            average = rate;
        // Still none: the time base's own rate (avformat_find_stream_info's last resort, the decoder having none).
        return new Rates(r ?? Reduce(timeBaseDen, timeBaseNum, int.MaxValue), average);
    }

    // libavformat's get_std_framerate: n/12 fps up to 30, then 31 to 60, 80, 120 and 240, all × 1001; then 24, 30, 60,
    // 12, 15 and 48 × 1000 (the 1000/1001 rates).
    static int StdFramerate(int i)
    {
        if (i < 30 * 12)
            return (i + 1) * 1001;
        i -= 30 * 12;
        if (i < 30)
            return (i + 31) * 1001 * 12;
        i -= 30;
        if (i < 3)
            return new[] { 80, 120, 240 }[i] * 1001 * 12;
        i -= 3;
        return new[] { 24, 30, 60, 12, 15, 48 }[i] * 1000 * 12;
    }

    static long Gcd(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    static long Rescale(long value, long num, long den) => (long)Math.Round((decimal)value * num * 1_000_000 / den, MidpointRounding.AwayFromZero);

    // av_reduce: num/den in lowest terms, within max (the best approximation when it isn't).
    static Fraction Reduce(long num, long den, long max)
    {
        long g = Gcd(num, den);
        num /= g;
        den /= g;
        if (num <= max && den <= max)
            return new Fraction(num, den);
        // Continued fractions, as av_reduce.
        long a0n = 0, a0d = 1, a1n = 1, a1d = 0;
        long n = num, d = den;
        while (d != 0)
        {
            long x = n / d, nextD = n - d * x;
            long a2n = x * a1n + a0n, a2d = x * a1d + a0d;
            if (a2n > max || a2d > max)
            {
                if (a1n != 0) x = (max - a0n) / a1n;
                if (a1d != 0) x = Math.Min(x, (max - a0d) / a1d);
                if (den * (2 * x * a1d + a0d) > num * a1d)
                    (a1n, a1d) = (x * a1n + a0n, x * a1d + a0d);
                break;
            }

            (a0n, a0d, a1n, a1d) = (a1n, a1d, a2n, a2d);
            (n, d) = (d, nextD);
        }

        return new Fraction(a1n, a1d);
    }
}
