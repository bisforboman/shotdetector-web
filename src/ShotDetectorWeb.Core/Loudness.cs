namespace ShotDetectorWeb;

/// <summary>
/// Loudness as ITU-R BS.1770-4 and EBU Tech 3341/3342 define it: integrated loudness in LUFS, loudness range in LU, and
/// true peak in dBTP. FFmpeg's <c>ebur128</c> filter (with <c>peak=true</c>) measures the same; NativeCheck compares the
/// two (README, Checks).
/// </summary>
/// <param name="Integrated">The integrated loudness (gated, LUFS); null when nothing is louder than the absolute gate (-70 LUFS).</param>
/// <param name="Range">The loudness range (LU): the spread of the short-term loudness, from its 10th to its 95th percentile; 0 when there is none.</param>
/// <param name="TruePeak">The highest true peak of any channel (dBTP).</param>
public sealed record Loudness(double? Integrated, double Range, double TruePeak);

/// <summary>
/// Measures <see cref="Loudness"/> a chunk at a time, holding a few numbers per 100 ms of audio (the gating blocks'
/// powers), never the samples. As FFmpeg's ebur128 filter does: K-weighting by the two biquads of BS.1770-4, computed for
/// the stream's own sample rate; gating blocks of 400 ms (momentary) and 3 s (short-term) every 100 ms, the first once a
/// block is full; channel weights 1, 0 for the LFE and 1.41 for the surround channels; the integrated loudness gated at
/// -70 LUFS and 10 LU below; the range gated at -70 LUFS and 20 LU below. The true peak is the sample peak and that of
/// the signal oversampled 4 times (2 times from 96 kHz), as BS.1770-4's Annex 2 describes.
/// </summary>
internal sealed class LoudnessMeter
{
    const double AbsoluteGate = -70;

    readonly int _channels;
    readonly double[] _weights;
    readonly Biquad _pre;
    readonly Biquad _rlb;
    readonly double[] _state;
    readonly int _step;
    int _inStep;
    readonly double[] _stepPower;
    readonly double[] _steps = new double[30];
    long _stepCount;
    readonly List<double> _momentary = [];
    readonly List<double> _shortTerm = [];
    readonly TruePeakMeter[] _truePeak;

    public LoudnessMeter(int sampleRate, int channels)
    {
        _channels = channels;
        _weights = new double[channels];
        for (int c = 0; c < channels; c++)
            _weights[c] = ChannelWeight(c, channels);

        // BS.1770-4's pre-filter (a high shelf) and RLB weighting (a high-pass), as their 48 kHz coefficients
        // reparametrised for any rate (libebur128's and FFmpeg's formulas).
        double k = Math.Tan(Math.PI * 1681.974450955533 / sampleRate);
        double q = 0.7071752369554196;
        double vh = Math.Pow(10, 3.999843853973347 / 20);
        double vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1 + k / q + k * k;
        _pre = new Biquad((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        k = Math.Tan(Math.PI * 38.13547087602444 / sampleRate);
        q = 0.5003270373238773;
        a0 = 1 + k / q + k * k;
        _rlb = new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        _state = new double[channels * 4];
        _step = Math.Max(sampleRate / 10, 1);
        _stepPower = new double[channels];
        _truePeak = new TruePeakMeter[channels];
        for (int c = 0; c < channels; c++)
            _truePeak[c] = new TruePeakMeter(sampleRate);
    }

    /// <summary>Each channel's true peak so far, as a linear amplitude (full scale 1).</summary>
    public double TruePeak(int channel) => _truePeak[channel].Peak;

    /// <summary>The next samples: one array per channel, <paramref name="count"/> of each.</summary>
    public void Add(float[][] samples, int count)
    {
        for (int c = 0; c < _channels; c++)
            _truePeak[c].Add(samples[c].AsSpan(0, count));

        int done = 0;
        while (done < count)
        {
            int n = Math.Min(count - done, _step - _inStep);
            for (int c = 0; c < _channels; c++)
                _stepPower[c] += Weigh(samples[c].AsSpan(done, n), c);
            done += n;
            _inStep += n;
            if (_inStep == _step)
                EndStep();
        }
    }

    public Loudness Finish()
    {
        double peak = 0;
        for (int c = 0; c < _channels; c++)
        {
            _truePeak[c].Finish();
            peak = Math.Max(peak, _truePeak[c].Peak);
        }

        return new Loudness(Integrated(), Range(), Dbfs(peak));
    }

    /// <summary>An amplitude in dB below full scale (1); -infinity for 0.</summary>
    public static double Dbfs(double amplitude) => 20 * Math.Log10(amplitude);

    /// <summary>BS.1770's loudness of a mean square: -0.691 + 10 log10.</summary>
    static double Lufs(double power) => -0.691 + 10 * Math.Log10(power);

    /// <summary>
    /// BS.1770-4's channel weights in ManagedCodecs' AAC output order (FL FR FC LFE BL BR, configuration 7 then SL SR; four
    /// channels FL FR FC BC): 1 for the front, 0 for the LFE, 1.41 for the rest, as FFmpeg's ebur128 weighs them.
    /// </summary>
    static double ChannelWeight(int channel, int channels) =>
        channel < 3 ? 1 : channel == 3 && channels is 6 or 8 ? 0 : 1.41;

    /// <summary>The sum of the K-weighted squares of one channel's samples, the filters' state carried on.</summary>
    double Weigh(ReadOnlySpan<float> samples, int channel)
    {
        var s = _state.AsSpan(channel * 4, 4);
        double y1 = s[0], y2 = s[1], z1 = s[2], z2 = s[3];
        var p = _pre;
        var r = _rlb;
        double sum = 0;
        foreach (float sample in samples)
        {
            // Transposed direct form II, as FFmpeg's filter runs them.
            double x = sample;
            double y = p.B0 * x + y1;
            y1 = p.B1 * x + y2 - p.A1 * y;
            y2 = p.B2 * x - p.A2 * y;
            double z = r.B0 * y + z1;
            z1 = r.B1 * y + z2 - r.A1 * z;
            z2 = r.B2 * y - r.A2 * z;
            sum += z * z;
        }

        s[0] = y1;
        s[1] = y2;
        s[2] = z1;
        s[3] = z2;
        return sum;
    }

    /// <summary>Every 100 ms: the 400 ms and 3 s blocks that end here, once they are full.</summary>
    void EndStep()
    {
        double power = 0;
        for (int c = 0; c < _channels; c++)
        {
            power += _weights[c] * _stepPower[c];
            _stepPower[c] = 0;
        }

        _steps[_stepCount % _steps.Length] = power;
        _stepCount++;
        _inStep = 0;
        if (_stepCount >= 4)
            _momentary.Add(Block(4));
        if (_stepCount >= 30)
            _shortTerm.Add(Block(30));
    }

    /// <summary>The mean square of the last <paramref name="steps"/> steps.</summary>
    double Block(int steps)
    {
        double sum = 0;
        for (int i = 1; i <= steps; i++)
            sum += _steps[(_stepCount - i) % _steps.Length];
        return sum / ((double)steps * _step);
    }

    /// <summary>BS.1770-4's gated loudness: blocks above -70 LUFS, then those above 10 LU below their mean.</summary>
    double? Integrated()
    {
        var above = _momentary.Where(p => Lufs(p) >= AbsoluteGate).ToList();
        if (above.Count == 0)
            return null;
        double gate = Lufs(above.Average()) - 10;
        var kept = above.Where(p => Lufs(p) >= gate).ToList();
        return kept.Count == 0 ? null : Lufs(kept.Average());
    }

    /// <summary>
    /// EBU Tech 3342's loudness range: the short-term blocks above -70 LUFS, then those above 20 LU below their mean; the
    /// 95th percentile's loudness less the 10th's (each the value of rank p × n / 100 rounded, as FFmpeg counts).
    /// </summary>
    double Range()
    {
        var above = _shortTerm.Where(p => Lufs(p) >= AbsoluteGate).ToList();
        if (above.Count == 0)
            return 0;
        double gate = Lufs(above.Average()) - 20;
        var kept = above.Select(Lufs).Where(l => l >= gate).Order().ToList();
        if (kept.Count == 0)
            return 0;
        double Percentile(int p) => kept[Math.Clamp((int)(p * kept.Count * 0.01 + 0.5), 1, kept.Count) - 1];
        return Percentile(95) - Percentile(10);
    }

    readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2);
}

/// <summary>
/// One channel's true peak: the largest magnitude of its samples and of the points between them, interpolated with a
/// windowed sinc (16 taps a phase) at 4 times the rate below 96 kHz, twice below 192 kHz. Only the stretches that could
/// beat the peak so far are interpolated (a stretch's largest sample times the filter's gain bounds what it can give).
/// </summary>
internal sealed class TruePeakMeter
{
    const int Half = 8;
    const int Block = 64;

    readonly int _factor;
    readonly float[][] _phases;
    readonly double _gain;

    // The samples not yet interpolated past, after the Half - 1 before them that their interpolation needs.
    float[] _buffer = new float[4096];
    int _count = Half - 1;

    public TruePeakMeter(int sampleRate)
    {
        _factor = sampleRate < 96000 ? 4 : sampleRate < 192000 ? 2 : 1;
        _phases = new float[_factor - 1][];
        for (int p = 1; p < _factor; p++)
        {
            // Phase p is the point p / factor of the way from a sample to the next: the sinc at its distance from each of
            // the 2 × Half samples around it, under a Kaiser window, scaled so that the taps add up to 1.
            var taps = new double[2 * Half];
            double t = p / (double)_factor;
            for (int j = 0; j < taps.Length; j++)
            {
                double x = t + Half - 1 - j;
                taps[j] = Sinc(x) * Kaiser(x / Half, 8);
            }

            double sum = taps.Sum();
            _phases[p - 1] = [.. taps.Select(v => (float)(v / sum))];
            _gain = Math.Max(_gain, _phases[p - 1].Sum(v => Math.Abs((double)v)));
        }
    }

    /// <summary>The true peak so far, a linear amplitude.</summary>
    public double Peak { get; private set; }

    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (float s in samples)
            Peak = Math.Max(Peak, Math.Abs(s));
        if (_factor == 1)
            return;
        if (_count + samples.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _count + samples.Length));
        samples.CopyTo(_buffer.AsSpan(_count));
        _count += samples.Length;
        Interpolate();
    }

    /// <summary>The last samples' points, against silence after them.</summary>
    public void Finish()
    {
        if (_factor == 1)
            return;
        Add(new float[Half]);
    }

    /// <summary>Every point between the samples that have Half samples after them; keeps the Half - 1 before the next.</summary>
    void Interpolate()
    {
        // Point between sample i and i + 1 uses samples i - Half + 1 to i + Half.
        int first = Half - 1;
        int end = _count - Half;
        for (int start = first; start < end; start += Block)
        {
            int stop = Math.Min(start + Block, end);
            var window = _buffer.AsSpan(start - Half + 1, stop - start + 2 * Half - 1);
            float largest = 0;
            foreach (float s in window)
                largest = Math.Max(largest, Math.Abs(s));
            if (largest * _gain <= Peak)
                continue;
            for (int i = start; i < stop; i++)
            {
                var around = _buffer.AsSpan(i - Half + 1, 2 * Half);
                foreach (var taps in _phases)
                {
                    float y = 0;
                    for (int j = 0; j < taps.Length; j++)
                        y += taps[j] * around[j];
                    Peak = Math.Max(Peak, Math.Abs(y));
                }
            }
        }

        if (end > first)
        {
            int keep = _count - (end - first);
            _buffer.AsSpan(end - first, keep).CopyTo(_buffer);
            _count = keep;
        }
    }

    static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    /// <summary>The Kaiser window at x in [-1, 1].</summary>
    static double Kaiser(double x, double beta) =>
        Math.Abs(x) >= 1 ? 0 : BesselI0(beta * Math.Sqrt(1 - x * x)) / BesselI0(beta);

    static double BesselI0(double x)
    {
        double sum = 1, term = 1;
        for (int k = 1; k < 50; k++)
        {
            term *= x / (2 * k) * (x / (2 * k));
            sum += term;
        }

        return sum;
    }
}
