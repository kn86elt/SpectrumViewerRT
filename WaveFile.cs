using System.Text;
using System.IO;

namespace SpectrumViewerRT;

public static class WaveFile
{
    public static void Save16BitMono(string path, IReadOnlyList<short> samples, int sampleRate)
    {
        Save16BitPcm(path, samples, sampleRate, channels: 1);
    }

    public static void Save16BitStereo(string path, IReadOnlyList<short> interleavedSamples, int sampleRate)
    {
        Save16BitPcm(path, interleavedSamples, sampleRate, channels: 2);
    }

    private static void Save16BitPcm(string path, IReadOnlyList<short> samples, int sampleRate, short channels)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        int dataBytes = samples.Count * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        foreach (var sample in samples)
            writer.Write(sample);
    }
}
