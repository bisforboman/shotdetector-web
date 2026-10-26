// The audio track's description (README, "Checks"): for each sample and each file in no-aac/ beside them,
// Mp4Input.Audio (the codec named from the headers, the rate and channels ManagedCodecs' demuxer reads, nothing decoded)
// against ffprobe's first audio stream (FrameReader's probe): codec_name, sample_rate and channels, or no audio at all.
// A Markdown table on standard output.
using FrameReader;

namespace ShotDetectorWeb.Checks;

static class AudioStreamCheck
{
    /// <summary>Checks each sample, and the files in no-aac/ beside them; returns how many disagreed.</summary>
    public static int Run(IReadOnlyList<string> samples)
    {
        string noAac = Path.Combine(Path.GetDirectoryName(samples.FirstOrDefault()) ?? "", "no-aac");
        var files = samples.Concat(Directory.Exists(noAac) ? Directory.GetFiles(noAac).Order() : []);
        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("| Sample | Audio track | FFmpeg's |");
        Console.WriteLine("| --- | --- | --- |");
        foreach (string file in files)
        {
            string ours, theirs;
            try
            {
                ours = Describe(Mp4Input.Open(file).Audio);
                var stream = MediaProbe.Probe(file).Streams.FirstOrDefault(s => s.Type == StreamType.Audio);
                theirs = Describe(stream is null ? null : new AudioStream(stream.Codec, stream.SampleRate ?? 0, stream.Channels ?? 0));
            }
            catch (Exception e)
            {
                ours = $"{e.GetType().Name}: {e.Message}";
                theirs = "";
            }

            bool same = ours == theirs;
            if (!same)
                failures++;
            Console.WriteLine($"| {Path.GetFileName(file)} | {ours} | {(same ? "same" : $"**DIFFERS: {theirs}**")} |");
        }

        return failures;
    }

    static string Describe(AudioStream? a) => a is null ? "none" : $"{a.Codec}, {a.SampleRate} Hz, {a.Channels} ch";
}
