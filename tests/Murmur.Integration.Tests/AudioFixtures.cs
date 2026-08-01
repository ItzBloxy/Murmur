namespace Murmur.Integration.Tests;

public static class AudioFixtures
{
    public const int SampleRate = 16000;

    /// <summary>Digital silence — the input that makes Whisper hallucinate subtitle credits.</summary>
    public static float[] Silence(double seconds) => new float[(int)(SampleRate * seconds)];

    public static string Directory =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string Path(string name) => System.IO.Path.Combine(Directory, name);

    public static bool Exists(string name) => File.Exists(Path(name));

    /// <summary>Reads a 16-bit PCM mono WAV at 16 kHz into float samples.</summary>
    public static float[] ReadWav(string name)
    {
        var bytes = File.ReadAllBytes(Path(name));

        // Locate the "data" chunk rather than assuming a fixed 44-byte header.
        var dataIndex = -1;
        for (var i = 12; i < bytes.Length - 8; i += 2)
        {
            if (bytes[i] == 'd' && bytes[i + 1] == 'a' && bytes[i + 2] == 't' && bytes[i + 3] == 'a')
            {
                dataIndex = i + 8;
                break;
            }
        }

        if (dataIndex < 0) throw new InvalidDataException($"No data chunk in {name}");

        var sampleCount = (bytes.Length - dataIndex) / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, dataIndex + i * 2) / 32768f;
        }

        return samples;
    }
}
